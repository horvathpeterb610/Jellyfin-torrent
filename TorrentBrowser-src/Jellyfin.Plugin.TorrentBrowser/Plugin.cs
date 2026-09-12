using Jellyfin.Plugin.TorrentBrowser.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.TorrentBrowser;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Torrent Browser";

    public override string Description =>
        "Browse a tracker's catalogue from the dashboard and send releases to your torrent client.";

    // Never change this after your first release: Jellyfin keys the stored
    // configuration file and the catalogue entry off it.
    public override Guid Id => Guid.Parse("20a80c3b-5977-4c19-87b6-bb7898e8e728");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;

        // The page whose Name matches the plugin Name becomes the "Settings"
        // page reachable from Dashboard -> Plugins.
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{ns}.Configuration.configPage.html"
        };

        // Any additional page is reachable at
        // /web/index.html#/configurationpage?name=TorrentBrowserBrowse
        yield return new PluginPageInfo
        {
            Name = "TorrentBrowserBrowse",
            EmbeddedResourcePath = $"{ns}.Configuration.browsePage.html"
        };
    }
}
