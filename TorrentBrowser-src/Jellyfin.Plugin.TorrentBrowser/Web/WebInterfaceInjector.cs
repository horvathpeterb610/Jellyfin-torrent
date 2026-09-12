using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TorrentBrowser.Web;

/// <summary>
/// Puts a "Torrents" button in the web client header, next to search and cast.
/// </summary>
/// <remarks>
/// Jellyfin has no server-side hook for the client's navigation — IHasWebPages
/// only reaches Dashboard, Plugins — so the only way in is to drop a script
/// beside index.html and reference it from there. A server upgrade replaces the
/// whole jellyfin-web folder and undoes both, which is why this runs on every
/// start rather than once at install.
/// </remarks>
public sealed class WebInterfaceInjector : IHostedService
{
    private const string ScriptFileName = "torrentbrowser-nav.js";

    /// <summary>Matches our own tag, including the whitespace in front of it,
    /// so re-running strips the old one instead of stacking a second copy.</summary>
    private static readonly Regex ExistingTag = new(
        @"\s*<script[^>]*torrentbrowser-nav\.js[^>]*>\s*</script>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<WebInterfaceInjector> _logger;

    public WebInterfaceInjector(IApplicationPaths appPaths, ILogger<WebInterfaceInjector> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Apply();
        }
        catch (UnauthorizedAccessException ex)
        {
            // The common case, not a bug: jellyfin-web lives under Program Files
            // on Windows, and the account the server runs as usually has read
            // and execute there but not write. Say what to do about it.
            _logger.LogWarning(
                "No write access to the web client at {Path}, so the Torrents button is missing. "
                + "Grant the account Jellyfin runs as Modify rights on that folder, or clear "
                + "\"Show a Torrents button in the header\" to stop trying. {Message}",
                _appPaths.WebPath,
                ex.Message);
        }
        catch (Exception ex)
        {
            // Never stop the server booting over a cosmetic button.
            _logger.LogWarning(ex, "Could not change the web interface. The Torrents button will be missing");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Apply()
    {
        // Read the instance once. Treating a null one as "disabled" would make
        // an initialisation order change quietly delete the button instead.
        var plugin = Plugin.Instance;

        if (plugin is null)
        {
            _logger.LogWarning("The plugin is not initialised, so the web interface was left alone");
            return;
        }

        var webPath = _appPaths.WebPath;
        var indexPath = Path.Combine(webPath, "index.html");

        if (!File.Exists(indexPath))
        {
            _logger.LogInformation(
                "No web client at {Path}, so the Torrents button was not added", webPath);
            return;
        }

        var scriptPath = Path.Combine(webPath, ScriptFileName);
        var html = File.ReadAllText(indexPath);

        // Strip any tag we wrote before, then add the current one back. That
        // keeps the file identical run to run and survives a version change.
        var updated = ExistingTag.Replace(html, string.Empty);

        if (plugin.Configuration.AddHeaderButton)
        {
            if (!File.Exists(scriptPath)
                || !string.Equals(File.ReadAllText(scriptPath), Script, StringComparison.Ordinal))
            {
                File.WriteAllText(scriptPath, Script);
            }

            var marker = updated.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);

            if (marker < 0)
            {
                _logger.LogWarning("index.html has no closing body tag, so the Torrents button was not added");
                return;
            }

            // The version makes the browser re-fetch the script after a plugin
            // update instead of serving the cached one.
            var tag = "<script defer src=\"" + ScriptFileName
                + "?v=" + plugin.Version.ToString() + "\"></script>";

            updated = updated.Insert(marker, tag + Environment.NewLine);
        }
        else if (File.Exists(scriptPath))
        {
            File.Delete(scriptPath);
        }

        if (string.Equals(updated, html, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(indexPath, updated);
        _logger.LogInformation("Updated {Path}. Reload the web client with Ctrl+F5 to pick it up", indexPath);
    }

    /// <summary>
    /// Written next to index.html. Kept here rather than as an embedded
    /// resource so there is no manifest name to get wrong.
    /// </summary>
    private const string Script = """
        (function () {
            // Added by the Jellyfin Torrent Browser plugin. Delete the file and
            // the <script> tag from index.html to remove it by hand.
            var BUTTON_ID = 'torrentBrowserHeaderButton';
            var TARGET = '#/configurationpage?name=TorrentBrowserBrowse';

            // The browse page renders in the dashboard layout and downloads are
            // admin-only by default, so only administrators get the button.
            var admin = 'unknown';

            function resolveAdmin() {
                if (admin === 'pending' || admin === 'yes' || admin === 'no') { return; }
                if (!window.ApiClient || !ApiClient.isLoggedIn || !ApiClient.isLoggedIn()) { return; }

                admin = 'pending';
                ApiClient.getCurrentUser().then(function (user) {
                    admin = (user && user.Policy && user.Policy.IsAdministrator) ? 'yes' : 'no';
                    schedule();
                }, function () {
                    admin = 'unknown';
                });
            }

            function addButton() {
                if (document.getElementById(BUTTON_ID)) { return; }

                var right = document.querySelector('.skinHeader .headerRight');
                if (!right) { return; }

                // Clone a real header button so this one inherits whatever
                // classes and structure the running Jellyfin version uses.
                var template = right.querySelector('button');
                if (!template) { return; }

                var button = template.cloneNode(true);
                button.id = BUTTON_ID;
                button.title = 'Torrents';
                button.setAttribute('aria-label', 'Torrents');

                // Drop the identifying classes, or the client can bind the
                // search or cast handler to the clone as well.
                Array.prototype.slice.call(button.classList).forEach(function (name) {
                    if (name !== 'headerButton' && /Button$/.test(name)) {
                        button.classList.remove(name);
                    }
                });

                var icon = button.querySelector('.material-icons');
                if (icon) { icon.textContent = 'cloud_download'; }

                button.addEventListener('click', function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    window.location.hash = TARGET;
                });

                right.insertBefore(button, right.firstChild);
            }

            function sync() {
                // Signed out or switched user: drop the button, ask again later.
                if (window.ApiClient && ApiClient.isLoggedIn && !ApiClient.isLoggedIn()) {
                    admin = 'unknown';
                    var existing = document.getElementById(BUTTON_ID);
                    if (existing) { existing.parentNode.removeChild(existing); }
                    return;
                }

                if (admin !== 'yes') {
                    resolveAdmin();
                    return;
                }

                addButton();
            }

            // The header is rebuilt on sign-in and on user switch, so watch for
            // it rather than running once. Coalesced to one check per frame,
            // because a subtree observer on body fires constantly.
            var queued = false;
            function schedule() {
                if (queued) { return; }
                queued = true;
                window.requestAnimationFrame(function () {
                    queued = false;
                    sync();
                });
            }

            new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true });
            document.addEventListener('viewshow', schedule);
            schedule();
        })();
        """;
}
