using System.Net;
using Jellyfin.Plugin.TorrentBrowser.Download;
using Jellyfin.Plugin.TorrentBrowser.Trackers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TorrentBrowser;

/// <summary>
/// Jellyfin finds this by convention and calls it during startup.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddMemoryCache(options => options.SizeLimit = 5_000);

        serviceCollection.AddSingleton<ReleaseCache>();

        serviceCollection.AddSingleton<TorznabTrackerClient>();
        serviceCollection.AddSingleton<TrackerClientResolver>();

        serviceCollection.AddSingleton<QBittorrentClient>();
        serviceCollection.AddSingleton<DownloadClientResolver>();

        // Plain client: Torznab, and fetching torrent files from public URLs.
        serviceCollection
            .AddHttpClient(TorrentBrowserHttp.ClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(45);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-TorrentBrowser/1.0");
            });

        // Session client: carries the tracker login cookie. The container is
        // owned by TorrentBrowserHttp, not by the handler, so the session
        // survives the factory recycling handlers.
        serviceCollection
            .AddHttpClient(TorrentBrowserHttp.SessionClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(45);
                // Some trackers serve a challenge page to unrecognised agents.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                CookieContainer = TorrentBrowserHttp.Cookies,
                UseCookies = true,
                AllowAutoRedirect = true
            });

        // qBittorrent client: its own cookie jar for the SID.
        serviceCollection
            .AddHttpClient(QBittorrentHttp.ClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                CookieContainer = QBittorrentClient.CookieJar,
                UseCookies = true,

                // Uncomment only if your client uses a self-signed certificate.
                // ServerCertificateCustomValidationCallback =
                //     HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
    }
}
