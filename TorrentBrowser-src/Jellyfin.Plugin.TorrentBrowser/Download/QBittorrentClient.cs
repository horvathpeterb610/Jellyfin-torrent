using System.Net;
using System.Net.Http.Headers;
using Jellyfin.Plugin.TorrentBrowser.Trackers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TorrentBrowser.Download;

/// <summary>
/// Talks to the qBittorrent Web API (v2). Sessions are cookie-based, so this
/// keeps its own CookieContainer and re-authenticates when the cookie expires.
/// </summary>
public sealed class QBittorrentClient : IDownloadClient
{
    private static readonly CookieContainer Cookies = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<QBittorrentClient> _logger;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private bool _loggedIn;

    public QBittorrentClient(IHttpClientFactory httpClientFactory, ILogger<QBittorrentClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name => "qBittorrent";

    internal static CookieContainer CookieJar => Cookies;

    public async Task AddAsync(TorrentRelease release, CancellationToken ct)
    {
        var config = Plugin.Instance!.Configuration;
        var baseUrl = NormaliseUrl(config.DownloadClientUrl);

        await EnsureLoggedInAsync(ct).ConfigureAwait(false);

        var category = release.Kind == ReleaseKind.Series
            ? config.SeriesCategory
            : config.MovieCategory;

        using var form = new MultipartFormDataContent();

        if (!string.IsNullOrWhiteSpace(release.DownloadUrl))
        {
            // Download the .torrent here rather than handing qBittorrent the URL.
            // On a private tracker the link is only valid with the session cookie
            // or passkey this plugin holds — qBittorrent fetching it directly
            // would get an HTML error page and silently add nothing.
            var bytes = await FetchTorrentFileAsync(release.DownloadUrl, ct).ConfigureAwait(false);

            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            form.Add(file, "torrents", SafeFileName(release.Title) + ".torrent");
        }
        else if (!string.IsNullOrWhiteSpace(release.MagnetUri))
        {
            form.Add(new StringContent(release.MagnetUri), "urls");
        }
        else
        {
            throw new InvalidOperationException("That release has no torrent file or magnet link.");
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            form.Add(new StringContent(category), "category");
        }

        if (!string.IsNullOrWhiteSpace(config.SavePath))
        {
            form.Add(new StringContent(config.SavePath), "savepath");
            form.Add(new StringContent("false"), "autoTMM");
        }

        var client = CreateClient(baseUrl);
        using var response = await client
            .PostAsync(baseUrl + "/api/v2/torrents/add", form, ct)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            _loggedIn = false;
            throw new InvalidOperationException("qBittorrent rejected the session. Try again.");
        }

        response.EnsureSuccessStatusCode();

        var body = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();

        // qBittorrent answers 200 with "Fails." when it could not parse the torrent.
        if (body.StartsWith("Fails", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("qBittorrent could not read that torrent file.");
        }

        _logger.LogInformation("Sent {Title} to qBittorrent in category {Category}", release.Title, category);
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        _loggedIn = false;
        await EnsureLoggedInAsync(ct).ConfigureAwait(false);

        var baseUrl = NormaliseUrl(Plugin.Instance!.Configuration.DownloadClientUrl);
        var client = CreateClient(baseUrl);

        var version = await client
            .GetStringAsync(baseUrl + "/api/v2/app/version", ct)
            .ConfigureAwait(false);

        return $"Connected to qBittorrent {version.Trim()}.";
    }

    private async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (_loggedIn)
        {
            return;
        }

        await _loginLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loggedIn)
            {
                return;
            }

            var config = Plugin.Instance!.Configuration;
            var baseUrl = NormaliseUrl(config.DownloadClientUrl);

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException("Set the qBittorrent address in the plugin settings first.");
            }

            var client = CreateClient(baseUrl);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = config.DownloadClientUsername,
                ["password"] = config.DownloadClientPassword
            });

            using var response = await client
                .PostAsync(baseUrl + "/api/v2/auth/login", content, ct)
                .ConfigureAwait(false);

            var body = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();

            if (!response.IsSuccessStatusCode || !body.Equals("Ok.", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    body.Contains("banned", StringComparison.OrdinalIgnoreCase)
                        ? "qBittorrent has temporarily banned this IP after failed logins. Wait, then retry."
                        : "qBittorrent rejected those credentials.");
            }

            _loggedIn = true;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    /// <summary>
    /// Uses the tracker session client so private-tracker download links work.
    /// </summary>
    private async Task<byte[]> FetchTorrentFileAsync(string url, CancellationToken ct)
    {
        var config = Plugin.Instance!.Configuration;

        var clientName = config.Mode == Configuration.TrackerMode.Scrape
            ? TorrentBrowserHttp.SessionClientName
            : TorrentBrowserHttp.ClientName;

        var client = _httpClientFactory.CreateClient(clientName);

        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        // A bencoded torrent starts with 'd'. Anything else means the tracker
        // handed back an HTML page — usually a login wall or a rate-limit notice.
        if (bytes.Length < 2 || bytes[0] != (byte)'d')
        {
            throw new InvalidOperationException(
                "The tracker returned a web page instead of a torrent file. The session may have expired.");
        }

        return bytes;
    }

    private HttpClient CreateClient(string baseUrl)
    {
        var client = _httpClientFactory.CreateClient(QBittorrentHttp.ClientName);

        // qBittorrent's CSRF check rejects requests whose Referer host does not
        // match its own, unless the user has disabled that protection.
        client.DefaultRequestHeaders.Referrer = new Uri(baseUrl);
        return client;
    }

    private static string NormaliseUrl(string url) => url.TrimEnd('/');

    private static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? "release" : cleaned[..Math.Min(cleaned.Length, 120)];
    }
}

public static class QBittorrentHttp
{
    public const string ClientName = "TorrentBrowser.QBittorrent";
}
