using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TorrentBrowser.Configuration;

public enum TrackerMode
{
    /// <summary>Talk to a Torznab endpoint (Prowlarr, Jackett, NZBHydra).</summary>
    Torznab = 0,

    /// <summary>Log into the tracker's website and scrape its search pages.</summary>
    Scrape = 1
}

public enum DownloadClientKind
{
    QBittorrent = 0,
    Transmission = 1,
    Deluge = 2
}

public class PluginConfiguration : BasePluginConfiguration
{
    // ---- Tracker -------------------------------------------------------

    public TrackerMode Mode { get; set; } = TrackerMode.Torznab;

    /// <summary>Base URL of the tracker or Torznab indexer, no trailing slash.</summary>
    public string TrackerUrl { get; set; } = string.Empty;

    /// <summary>Torznab API key. Used when <see cref="Mode"/> is Torznab.</summary>
    public string TorznabApiKey { get; set; } = string.Empty;

    /// <summary>Tracker account. Used when <see cref="Mode"/> is Scrape.</summary>
    public string TrackerUsername { get; set; } = string.Empty;

    /// <summary>
    /// Tracker password. NOTE: plugin configuration is written to
    /// config/plugins/configurations/Jellyfin.Plugin.TorrentBrowser.xml as
    /// plain text. See README for what that means and how to avoid it.
    /// </summary>
    public string TrackerPassword { get; set; } = string.Empty;

    /// <summary>Minimum seconds between requests to the tracker.</summary>
    public int RequestIntervalSeconds { get; set; } = 2;

    // ---- Download client -----------------------------------------------

    public DownloadClientKind DownloadClient { get; set; } = DownloadClientKind.QBittorrent;

    /// <summary>e.g. http://127.0.0.1:8080 — no trailing slash.</summary>
    public string DownloadClientUrl { get; set; } = string.Empty;

    public string DownloadClientUsername { get; set; } = string.Empty;

    public string DownloadClientPassword { get; set; } = string.Empty;

    /// <summary>Category applied to movie torrents, e.g. "radarr" or "movies".</summary>
    public string MovieCategory { get; set; } = "movies";

    /// <summary>Category applied to series torrents.</summary>
    public string SeriesCategory { get; set; } = "series";

    /// <summary>Optional save path override sent to the client. Empty = client default.</summary>
    public string SavePath { get; set; } = string.Empty;

    // ---- Behaviour ------------------------------------------------------

    /// <summary>How long search results stay resolvable by the download route.</summary>
    public int ResultCacheMinutes { get; set; } = 30;

    /// <summary>Restrict sending torrents to administrators only.</summary>
    public bool RequireAdminToDownload { get; set; } = true;

    // ---- Web interface ---------------------------------------------------

    /// <summary>
    /// Adds a Torrents button to the web client header. Applied at server
    /// start, because it works by editing jellyfin-web's index.html.
    /// </summary>
    public bool AddHeaderButton { get; set; } = true;
}
