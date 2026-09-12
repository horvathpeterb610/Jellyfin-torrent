using System.Net;

namespace Jellyfin.Plugin.TorrentBrowser.Trackers;

public static class TorrentBrowserHttp
{
    /// <summary>Plain client: no cookies, used for Torznab and download clients.</summary>
    public const string ClientName = "TorrentBrowser";

    /// <summary>Cookie-bearing client used for the scraped tracker session.</summary>
    public const string SessionClientName = "TorrentBrowser.Session";

    /// <summary>
    /// Held outside the handler on purpose. IHttpClientFactory rotates the
    /// primary handler every couple of minutes; if the CookieContainer lived on
    /// the handler, the tracker session would silently drop and every other
    /// search would bounce to the login page.
    /// </summary>
    public static CookieContainer Cookies { get; } = new();

    public static void ClearCookies()
    {
        foreach (Cookie cookie in Cookies.GetAllCookies())
        {
            cookie.Expired = true;
        }
    }
}
