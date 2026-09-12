using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.TorrentBrowser.Trackers;

/// <summary>
/// Remembers the releases this server handed to the browse UI so the download
/// route can take an opaque id instead of a URL.
///
/// The reason this exists rather than just posting the torrent URL back: an
/// endpoint that accepts an arbitrary URL and fetches it server-side is a
/// server-side request forgery hole. Any authenticated user could point it at
/// internal addresses your Jellyfin box can reach. Issuing ids means the route
/// can only ever fetch something a search already returned.
/// </summary>
public sealed class ReleaseCache
{
    private readonly IMemoryCache _cache;

    public ReleaseCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public void Remember(IEnumerable<TorrentRelease> releases)
    {
        var minutes = Math.Max(Plugin.Instance?.Configuration.ResultCacheMinutes ?? 30, 1);

        foreach (var release in releases)
        {
            _cache.Set(
                Key(release.Id),
                release,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(minutes),
                    Size = 1
                });
        }
    }

    public TorrentRelease? Get(string id) =>
        _cache.TryGetValue<TorrentRelease>(Key(id), out var release) ? release : null;

    private static string Key(string id) => "torrentbrowser:release:" + id;
}
