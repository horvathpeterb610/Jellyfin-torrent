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
- jellyfin-web sits under C:\Program Files, where the Jellyfin account has no
  write access, so WebInterfaceInjector fails with UnauthorizedAccessException
  until that folder is granted Modify for that account.
- Embedded resource names come from folder paths. Keep Api/, Configuration/,
  Download/, Trackers/ — flattening breaks the build.

- Emoji flags do not render on Windows; the browse page draws them in CSS.
  Same for icons: material-icons ligatures show as the literal word if the
  font misses, so the browse page uses inline SVG.
- Torznab has no sort parameter — the indexer answers newest first, so the
  browse page buffers several indexer pages, then sorts, filters and pages
  through that buffer client-side. Opening it pulls 3 pages in the background.
- Android TV, Fire TV and Roku are native apps with no web dashboard: no
  server plugin can put UI in them. LG webOS and Samsung Tizen run
  jellyfin-web in a webview, so plugin pages do work there. The browse page
  is therefore built to be driven by a remote in any browser-based client:
  arrow keys walk the card grid, OK sends, and /TorrentBrowser redirects to
  it so the URL can be typed on an on-screen keyboard.
- Never disable a button the user is focused on. Disabling blurs it, and on
  a remote that drops focus to the document — you start again from the top
  of the page after every send. Guard on state instead.
- Posters come from Jellyfin's own Items/RemoteSearch (admin-only, one
  provider call per release) — lazy, queued three at a time, cached per
  session, and it stops asking after a 403.

## Not done yet
- Transmission and Deluge clients (IDownloadClient is two methods)
