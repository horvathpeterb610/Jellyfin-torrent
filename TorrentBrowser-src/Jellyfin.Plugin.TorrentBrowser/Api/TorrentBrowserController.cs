using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using Jellyfin.Plugin.TorrentBrowser.Download;
using Jellyfin.Plugin.TorrentBrowser.Trackers;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TorrentBrowser.Api;

/// <summary>
/// Jellyfin discovers controllers in plugin assemblies automatically. These
/// routes live under the server root, e.g. GET /TorrentBrowser/Search.
/// </summary>
[ApiController]
// Jellyfin 10.11 no longer exposes Policies.DefaultAuthorization. A bare
// [Authorize] falls through to the framework default policy, which requires an
// authenticated user under Jellyfin's own scheme — the semantics we want here.
[Authorize]
[Route("TorrentBrowser")]
[Produces(MediaTypeNames.Application.Json)]
public class TorrentBrowserController : ControllerBase
{
    private readonly TrackerClientResolver _trackers;
    private readonly DownloadClientResolver _downloaders;
    private readonly ReleaseCache _cache;
    private readonly ILogger<TorrentBrowserController> _logger;

    public TorrentBrowserController(
        TrackerClientResolver trackers,
        DownloadClientResolver downloaders,
        ReleaseCache cache,
        ILogger<TorrentBrowserController> logger)
    {
        _trackers = trackers;
        _downloaders = downloaders;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Short link to the browse page: GET /TorrentBrowser redirects to it.
    /// </summary>
    /// <remarks>
    /// Anonymous because the browser arrives here with no token and this only
    /// ever points at a static page, which asks for a login itself. It exists
    /// because /web/index.html#/configurationpage?name=TorrentBrowserBrowse is
    /// not a URL anyone is going to type on a TV's on-screen keyboard.
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult Browse() =>
        Redirect(Request.PathBase.ToUriComponent()
            + "/web/index.html#/configurationpage?name=TorrentBrowserBrowse");

    /// <summary>Searches the configured tracker.</summary>
    [HttpGet("Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchResponse>> Search(
        [FromQuery] string? query,
        [FromQuery] string? kind,
        [FromQuery] int page = 1,
        [FromQuery] int? season = null,
        [FromQuery] int? episode = null,
        CancellationToken ct = default)
    {
        var releaseKind = kind?.ToLowerInvariant() switch
        {
            "movie" => ReleaseKind.Movie,
            "series" => ReleaseKind.Series,
            _ => ReleaseKind.Unknown
        };

        try
        {
            var client = _trackers.Resolve();

            var results = await client.SearchAsync(
                new TrackerQuery
                {
                    SearchTerm = query,
                    Kind = releaseKind,
                    Page = Math.Max(page, 1),
                    Season = season,
                    Episode = episode
                },
                ct).ConfigureAwait(false);

            _cache.Remember(results);

            return Ok(new SearchResponse
            {
                Page = page,
                Items = results.Select(ReleaseDto.From).ToList()
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tracker search failed");
            return Problem(title: "Search failed", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>Sends a previously returned release to the torrent client.</summary>
    [HttpPost("Releases/{releaseId}/Download")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Download(
        [FromRoute, Required] string releaseId,
        CancellationToken ct = default)
    {
        if (Plugin.Instance!.Configuration.RequireAdminToDownload && !User.IsInRole("Administrator"))
        {
            return Forbid();
        }

        var release = _cache.Get(releaseId);

        if (release is null)
        {
            return NotFound(new
            {
                Message = "Those results have expired. Search again, then send the release."
            });
        }

        try
        {
            await _downloaders.Resolve().AddAsync(release, ct).ConfigureAwait(false);
            return NoContent();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not send {Title} to the torrent client", release.Title);
            return Problem(title: "Send failed", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>Checks the tracker settings. Admin only.</summary>
    [HttpPost("Test/Tracker")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<TestResponse>> TestTracker(CancellationToken ct = default) =>
        await RunTest(() => _trackers.Resolve().TestAsync(ct)).ConfigureAwait(false);

    /// <summary>Checks the torrent client settings. Admin only.</summary>
    [HttpPost("Test/DownloadClient")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<TestResponse>> TestDownloadClient(CancellationToken ct = default) =>
        await RunTest(() => _downloaders.Resolve().TestAsync(ct)).ConfigureAwait(false);

    private async Task<ActionResult<TestResponse>> RunTest(Func<Task<string>> test)
    {
        try
        {
            return Ok(new TestResponse { Ok = true, Message = await test().ConfigureAwait(false) });
        }
        catch (Exception ex)
        {
            return Ok(new TestResponse { Ok = false, Message = ex.Message });
        }
    }
}

public sealed class SearchResponse
{
    public int Page { get; set; }

    public IReadOnlyList<ReleaseDto> Items { get; set; } = Array.Empty<ReleaseDto>();
}

public sealed class TestResponse
{
    public bool Ok { get; set; }

    public string Message { get; set; } = string.Empty;
}

/// <summary>What the browse page receives. Deliberately excludes the real
/// download URL and any passkey embedded in it.</summary>
public sealed class ReleaseDto
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public DateTimeOffset? PublishDate { get; set; }

    public string? DetailsUrl { get; set; }

    public string? ImdbId { get; set; }

    public static ReleaseDto From(TorrentRelease release) => new()
    {
        Id = release.Id,
        Title = release.Title,
        SizeBytes = release.SizeBytes,
        Seeders = release.Seeders,
        Leechers = release.Leechers,
        PublishDate = release.PublishDate,
        DetailsUrl = release.DetailsUrl,
        ImdbId = release.ImdbId
    };
}
