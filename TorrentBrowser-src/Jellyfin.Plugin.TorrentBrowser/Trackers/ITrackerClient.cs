namespace Jellyfin.Plugin.TorrentBrowser.Trackers;

public interface ITrackerClient
{
    string Name { get; }

    Task<IReadOnlyList<TorrentRelease>> SearchAsync(TrackerQuery query, CancellationToken ct);

    /// <summary>Verifies URL and credentials. Returns a message suitable for the UI.</summary>
    Task<string> TestAsync(CancellationToken ct);
}

/// <summary>
/// Picks the concrete client based on configuration at call time, so changing
/// the setting takes effect without restarting the server.
///
/// Only Torznab is implemented. A direct scraping client used to live here; it
/// was dropped along with its AngleSharp dependency. To add one back, implement
/// ITrackerClient, register it in PluginServiceRegistrator, and return it for
/// TrackerMode.Scrape below.
/// </summary>
public sealed class TrackerClientResolver
{
    private readonly TorznabTrackerClient _torznab;

    public TrackerClientResolver(TorznabTrackerClient torznab)
    {
        _torznab = torznab;
    }

    public ITrackerClient Resolve()
    {
        var config = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Plugin is not initialised.");

        if (config.Mode != Configuration.TrackerMode.Torznab)
        {
            throw new NotSupportedException(
                "Direct tracker scraping is not built into this version. "
                + "Set 'Connect using' to a Torznab indexer.");
        }

        return _torznab;
    }
}
