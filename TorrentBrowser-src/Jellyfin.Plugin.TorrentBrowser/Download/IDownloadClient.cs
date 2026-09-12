using Jellyfin.Plugin.TorrentBrowser.Trackers;

namespace Jellyfin.Plugin.TorrentBrowser.Download;

public interface IDownloadClient
{
    string Name { get; }

    Task AddAsync(TorrentRelease release, CancellationToken ct);

    Task<string> TestAsync(CancellationToken ct);
}

public sealed class DownloadClientResolver
{
    private readonly QBittorrentClient _qbittorrent;

    public DownloadClientResolver(QBittorrentClient qbittorrent)
    {
        _qbittorrent = qbittorrent;
    }

    public IDownloadClient Resolve()
    {
        var config = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Plugin is not initialised.");

        return config.DownloadClient switch
        {
            Configuration.DownloadClientKind.QBittorrent => _qbittorrent,

            // Transmission: POST to /transmission/rpc with a torrent-add method.
            // It answers the first call with 409 and an X-Transmission-Session-Id
            // header that you must echo on the retry.
            //
            // Deluge: POST to /json with auth.login then core.add_torrent_file.
            //
            // Both follow the same shape as QBittorrentClient — add a class,
            // implement IDownloadClient, register it, and add a case here.
            _ => throw new NotSupportedException(
                $"{config.DownloadClient} is not implemented yet. Select qBittorrent.")
        };
    }
}
