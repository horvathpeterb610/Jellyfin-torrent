# Torrent Browser — Jellyfin plugin

Jellyfin 10.11.x, .NET 9. Build machine is Windows; Jellyfin runs on a
separate Windows server at C:\ProgramData\Jellyfin\Server\.

Deploy: stop Jellyfin (it locks loaded DLLs), copy
Jellyfin.Plugin.TorrentBrowser.dll + meta.json to
<config>\plugins\TorrentBrowser\, start. Do NOT copy MediaBrowser.*,
Microsoft.*, System.* or other Jellyfin.* DLLs from publish output —
the server has them, and copies make it load its own code twice.

Indexer is Prowlarr (nCore). URL format is http://host:9696/<indexerId>
with NO /api — the plugin appends it.

## Already learned the hard way
- Policies.DefaultAuthorization doesn't exist in 10.11. Use bare [Authorize].
  Policies.RequiresElevation still works.
- Jellyfin serialises enums as NAMES, not ordinals. <select> option values
  must be the C# member names or save returns 500.
- Prowlarr serves t=caps without checking the API key, so caps-based
  connection tests pass with a wrong key. Test with a real search.
- Use t=search, not t=movie. Many indexers don't implement the typed modes
  and Prowlarr errors rather than falling back.
- No plugin API reaches the web client's sidebar, header or home rows.
  Web/WebInterfaceInjector.cs writes a script into jellyfin-web and adds a
  tag to index.html at startup; a server upgrade wipes both, hence every start.
- Embedded resource names come from folder paths. Keep Api/, Configuration/,
  Download/, Trackers/ — flattening breaks the build.

## Not done yet
- Transmission and Deluge clients (IDownloadClient is two methods)
- Pagination on the browse page (API already takes `page`)
- Poster art via the IMDb id Torznab returns
