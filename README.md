Torrent Browser — Jellyfin plugin scaffold

A working skeleton for the three things you described: read a tracker's catalogue, show it, and send a release to a torrent client on click. Credentials are set per server in the plugin settings.

Targets Jellyfin 10.11.x (.NET 9). Jellyfin 12.0 moved to .NET 10 and is not ABI-compatible — you will need a second build with net10.0 and a separate targetAbi entry in your manifest.

The decision that shapes everything else

There are two places a plugin can put content in Jellyfin, and they are not equally suited to this.

IChannel puts a browsable folder in the main library UI, with posters and metadata. It looks like the obvious fit. It isn't, for one reason: a channel item is something Jellyfin expects to play. There is no supported hook for "put a Download button on this tile". You would end up either injecting JavaScript into jellyfin-web (fragile, breaks on every server update) or modelling each release as a folder containing a fake "click me to download" child item. Both are worse than the alternative.

A plugin page plus an API controller — what this scaffold does — gives you a real search UI with a real button, at the cost of living under Dashboard → Plugins rather than in the main library grid.

The honest summary: build the page first, because it works. If you still want the library-grid browsing experience afterwards, add an IChannel on top that reuses the same ITrackerClient for browsing only, and leave downloading on the page.

A third option worth knowing about: don't render your own UI at all. Jellyseerr already solves "browse and request", and several plugins just bridge to it.

How it reads the tracker

TorznabTrackerClient talks to Prowlarr or Jackett. Those projects maintain login flows, rate limits, category mappings and Cloudflare handling for several hundred trackers, and expose all of them behind one stable XML API. Your plugin has no tracker-specific code at all, and nothing breaks when a tracker redesigns its pages.

An earlier version of this scaffold also shipped a ScrapeTrackerClient that logged in with a username and password and parsed the search page directly. It was removed: every selector in it was specific to one site, its AngleSharp dependency carried an open advisory, and Prowlarr does the job better. The TrackerMode enum and the username/password fields remain in PluginConfiguration so you can add one back — implement ITrackerClient, register it, and return it from TrackerClientResolver.

Layout
Plugin.cs                         Entry point, registers two dashboard pages
PluginServiceRegistrator.cs       DI wiring, three named HttpClients
Configuration/
  PluginConfiguration.cs          Settings model
  configPage.html                 Settings UI
  browsePage.html                 Search + download UI
Trackers/
  ITrackerClient.cs               Abstraction + resolver
  TorrentRelease.cs               Release model, opaque id
  TorznabTrackerClient.cs         Prowlarr / Jackett
  ReleaseCache.cs                 Maps ids back to releases
  TorrentBrowserHttp.cs           Client names, shared cookie jar
Download/
  IDownloadClient.cs              Abstraction + resolver
  QBittorrentClient.cs            qBittorrent Web API v2
Api/
  TorrentBrowserController.cs     /TorrentBrowser/* routes
Routes
Method	Route	Who
GET	/TorrentBrowser/Search?query=&kind=movie|series&page=1	any signed-in user
POST	/TorrentBrowser/Releases/{id}/Download	admin by default
POST	/TorrentBrowser/Test/Tracker	admin
POST	/TorrentBrowser/Test/DownloadClient	admin
Build and install
bash
dotnet publish -c Release

Copy your assembly and its third-party dependencies only — here that is Jellyfin.Plugin.TorrentBrowser.dll — plus meta.json, into <jellyfin-config>/plugins/TorrentBrowser/, then restart. With the scraper removed this build has no third-party dependencies, so that is two files.

Do not copy the MediaBrowser.*, Microsoft.*, System.* or other Jellyfin.* assemblies that dotnet publish also produces. The server already has those, and dropping copies into a plugin folder makes it load its own code twice. The visible symptom is the dashboard showing the plugin under the wrong name with the server's version number instead of yours.

Missing a genuine third-party dependency causes the opposite failure — the assembly won't load at all. Dashboard → Logs will name it.

On Windows the plugin directory is usually C:\ProgramData\Jellyfin\Server\plugins\. Stop the server before overwriting a DLL: Windows locks loaded assemblies, so the copy fails and you end up testing the previous build.

For development, bind-mount the build output into a container:

bash
docker run -d --name jellyfin-dev -p 8096:8096 \
  -v $(pwd)/dev-config:/config \
  -v $(pwd)/bin/Release/net9.0:/config/plugins/TorrentBrowser \
  jellyfin/jellyfin:10.11

For distribution, use JPRM to produce the zip and manifest.json. Set targetAbi to the oldest server you compiled against.

Four things that will bite you

Passwords are stored in plain text. Jellyfin serialises plugin configuration to config/plugins/configurations/Jellyfin.Plugin.TorrentBrowser.xml as XML, with no encryption, and the config page reads them back over the API. There is no secret store in the plugin API. If that matters, take an environment variable or a file path in the config instead of the password itself, and read the credential at runtime — then a backup or a compromised admin account doesn't leak it. The settings page says so plainly rather than pretending otherwise.

Don't let the download route take a URL. The obvious design is to post the torrent URL back from the browser. That turns your endpoint into a server-side request forgery primitive: any signed-in user can make Jellyfin fetch arbitrary internal addresses. This scaffold issues opaque ids from ReleaseCache instead, so the route can only fetch something a search already returned. Keep that property if you refactor.

Fetch the .torrent yourself, don't hand qBittorrent the link. On a private tracker the download URL only works with your session cookie or passkey. qBittorrent fetching it directly gets an HTML login page and adds nothing, with no useful error. QBittorrentClient.FetchTorrentFileAsync downloads the bytes through the authenticated client and uploads them as a file, and checks the first byte is d (bencode) to catch the failure early.

Enums cross the API as names, not numbers. getPluginConfiguration returns Mode: "Torznab", not 0. Bind a <select> whose option values are "0" and "1" and it renders blank, then parseInt("") sends NaN, which serialises to null, which will not bind to a non-nullable enum — a 500 on save with nothing useful in the browser console. The config page uses the C# member names as option values and accepts either form when loading.

Policies.DefaultAuthorization is gone in 10.11. A bare [Authorize] uses the framework default policy, which requires an authenticated user — the same thing. Policies.RequiresElevation still exists, so keep the MediaBrowser.Common.Api using. On older servers the class lives in Jellyfin.Api.Constants instead.

The test buttons read saved settings. They call the server, which reads the configuration file, not the form. Save before testing or you are testing the previous values.

What's left to write
Transmission and Deluge clients — IDownloadClient is two methods; the resolver has notes on each protocol's quirk.
Pagination in the browse page (the API already takes page).
Poster art. Torznab gives you an IMDb id, so you can hand it to Jellyfin's own metadata providers rather than scraping images.
A scheduled task that re-runs saved searches, if you want "new releases" rather than search-on-demand. Implement IScheduledTask.
One non-technical note

Whether the content on a given tracker is legal to download depends entirely on the tracker and where you are — a plugin like this is equally a front-end for Linux distributions, archive.org, and public-domain film collections as it is for anything else. That's your call to make, not something the code decides. Worth being deliberate about it before you point it at a public index, especially if other people use your server.
