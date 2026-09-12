using System.Globalization;
using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TorrentBrowser.Trackers;

/// <summary>
/// Reads a Torznab feed. Prowlarr and Jackett both expose one per indexer and
/// handle the tracker login, cookies, rate limits and Cloudflare for you, so
/// this is the path that stays working when the tracker changes its HTML.
/// </summary>
public sealed class TorznabTrackerClient : ITrackerClient
{
    private static readonly XNamespace Torznab = "http://torznab.com/schemas/2015/feed";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TorznabTrackerClient> _logger;

    public TorznabTrackerClient(
        IHttpClientFactory httpClientFactory,
        ILogger<TorznabTrackerClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name => "Torznab";

    public async Task<IReadOnlyList<TorrentRelease>> SearchAsync(TrackerQuery query, CancellationToken ct)
    {
        var config = Plugin.Instance!.Configuration;

        if (string.IsNullOrWhiteSpace(config.TrackerUrl))
        {
            throw new InvalidOperationException("Set the indexer URL in the plugin settings first.");
        }

        // t=search is the generic mode every indexer implements. The typed modes
        // (t=movie, t=tvsearch) add imdbid/season/ep but plenty of indexers do
        // not advertise them, and Prowlarr answers an unsupported mode with an
        // error rather than falling back. Only ask for tvsearch when season or
        // episode is actually set; the category filter does the rest.
        var function = query.Kind == ReleaseKind.Series && (query.Season.HasValue || query.Episode.HasValue)
            ? "tvsearch"
            : "search";

        var categories = query.Kind switch
        {
            ReleaseKind.Movie => "2000",
            ReleaseKind.Series => "5000",
            _ => "2000,5000"
        };

        var parameters = new Dictionary<string, string?>
        {
            ["t"] = function,
            ["apikey"] = config.TorznabApiKey,
            ["cat"] = categories,
            ["limit"] = query.Limit.ToString(CultureInfo.InvariantCulture),
            ["offset"] = ((Math.Max(query.Page, 1) - 1) * query.Limit).ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            parameters["q"] = query.SearchTerm;
        }

        if (query.Season.HasValue)
        {
            parameters["season"] = query.Season.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (query.Episode.HasValue)
        {
            parameters["ep"] = query.Episode.Value.ToString(CultureInfo.InvariantCulture);
        }

        var url = BuildUrl(config.TrackerUrl.TrimEnd('/') + "/api", parameters);

        var client = _httpClientFactory.CreateClient(TorrentBrowserHttp.ClientName);
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        ThrowIfUnauthorized(response);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct).ConfigureAwait(false);

        // Torznab reports auth and parameter errors as a 200 with an <error> body.
        var error = doc.Root?.Name.LocalName == "error" ? doc.Root : null;
        if (error is not null)
        {
            throw new InvalidOperationException(
                error.Attribute("description")?.Value ?? "The indexer rejected the request.");
        }

        var releases = new List<TorrentRelease>();

        foreach (var item in doc.Descendants("item"))
        {
            var attributes = ReadAttributes(item);

            var enclosureUrl = item.Element("enclosure")?.Attribute("url")?.Value;
            var link = item.Element("link")?.Value;
            var candidate = enclosureUrl ?? link;

            var isMagnet = candidate?.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) == true;

            releases.Add(new TorrentRelease
            {
                Title = item.Element("title")?.Value ?? "Untitled",
                DownloadUrl = isMagnet ? null : candidate,
                MagnetUri = isMagnet ? candidate : GetValue(attributes, "magneturl"),
                DetailsUrl = item.Element("comments")?.Value ?? item.Element("guid")?.Value,
                SizeBytes = ParseLong(item.Element("size")?.Value ?? GetValue(attributes, "size")),
                Seeders = ParseInt(GetValue(attributes, "seeders")),
                Leechers = ParseInt(GetValue(attributes, "peers")) - ParseInt(GetValue(attributes, "seeders")),
                PublishDate = ParseDate(item.Element("pubDate")?.Value),
                ImdbId = NormaliseImdb(GetValue(attributes, "imdbid") ?? GetValue(attributes, "imdb")),
                Kind = query.Kind == ReleaseKind.Unknown ? KindFromCategories(item) : query.Kind
            });
        }

        _logger.LogDebug("Torznab returned {Count} releases for {Term}", releases.Count, query.SearchTerm);
        return releases;
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        var config = Plugin.Instance!.Configuration;

        // The test button runs against the SAVED configuration, so an unsaved
        // form reaches here with an empty URL. Without this guard HttpClient
        // throws "An invalid request URI was provided", which tells the user
        // nothing about what to do.
        if (string.IsNullOrWhiteSpace(config.TrackerUrl))
        {
            throw new InvalidOperationException("Save the indexer URL before testing.");
        }

        // Deliberately a real search rather than t=caps. Prowlarr serves caps
        // WITHOUT checking the API key, so a caps-based test passes even when the
        // key is wrong — it only ever proved the URL was reachable.
        var url = BuildUrl(
            config.TrackerUrl.TrimEnd('/') + "/api",
            new Dictionary<string, string?>
            {
                ["t"] = "search",
                ["apikey"] = config.TorznabApiKey,
                ["limit"] = "1"
            });

        var client = _httpClientFactory.CreateClient(TorrentBrowserHttp.ClientName);
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        ThrowIfUnauthorized(response);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var doc = XDocument.Parse(body);

        if (doc.Root?.Name.LocalName == "error")
        {
            throw new InvalidOperationException(
                doc.Root.Attribute("description")?.Value ?? "The indexer rejected the API key.");
        }

        var server = doc.Root?.Element("server")?.Attribute("title")?.Value;
        return server is null ? "Connected." : $"Connected to {server}.";
    }

    /// <summary>
    /// Torznab usually reports problems as a 200 with an &lt;error&gt; body, but a
    /// bad API key comes back as a bare 401 that EnsureSuccessStatusCode would
    /// turn into an unreadable HttpRequestException.
    /// </summary>
    private static void ThrowIfUnauthorized(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized
            || response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "The indexer rejected the API key. Copy it from Prowlarr under Settings, General.");
        }
    }

    private static string BuildUrl(string baseUrl, Dictionary<string, string?> parameters)
    {
        var query = string.Join(
            '&',
            parameters
                .Where(p => !string.IsNullOrEmpty(p.Value))
                .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));

        return $"{baseUrl}?{query}";
    }

    /// <summary>
    /// Flattens the &lt;torznab:attr&gt; elements of one item into a lookup.
    /// </summary>
    /// <remarks>
    /// Torznab repeats the element for multi-valued fields — a release carries
    /// one "category" attr per category it belongs to, so nCore movies arrive
    /// with 2000 and 2040 — which made a plain ToDictionary throw "An item with
    /// the same key has already been added. Key: category" and failed the whole
    /// search with a 502. The first non-empty value wins: Prowlarr lists the
    /// broad category before the specific one, and repeats of any other
    /// attribute are alternates rather than corrections.
    /// </remarks>
    private static Dictionary<string, string> ReadAttributes(XElement item)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attr in item.Elements(Torznab + "attr"))
        {
            var name = attr.Attribute("name")?.Value;
            var value = attr.Attribute("value")?.Value;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value))
            {
                continue;
            }

            attributes.TryAdd(name, value);
        }

        return attributes;
    }

    /// <summary>
    /// Reads the Newznab category numbers off an item so a mixed search can
    /// still tell a film from a series — that decides which qBittorrent
    /// category the release is sent to.
    /// </summary>
    private static ReleaseKind KindFromCategories(XElement item)
    {
        foreach (var attr in item.Elements(Torznab + "attr"))
        {
            if (!string.Equals(attr.Attribute("name")?.Value, "category", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(
                    attr.Attribute("value")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var category))
            {
                continue;
            }

            if (category is >= 5000 and < 6000)
            {
                return ReleaseKind.Series;
            }

            if (category is >= 2000 and < 3000)
            {
                return ReleaseKind.Movie;
            }
        }

        return ReleaseKind.Unknown;
    }

    private static string? GetValue(Dictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : null;

    private static string? NormaliseImdb(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
            ? value
            : "tt" + value.PadLeft(7, '0');
    }
}
