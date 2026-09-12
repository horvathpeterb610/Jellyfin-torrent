using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.TorrentBrowser.Trackers;

public enum ReleaseKind
{
    Unknown = 0,
    Movie = 1,
    Series = 2
}

public sealed class TorrentRelease
{
    public required string Title { get; init; }

    /// <summary>Direct link to the .torrent file, if the tracker offers one.</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>Magnet URI, if the tracker offers one.</summary>
    public string? MagnetUri { get; init; }

    /// <summary>Tracker page for this release, for the "details" link in the UI.</summary>
    public string? DetailsUrl { get; init; }

    public long SizeBytes { get; init; }

    public int Seeders { get; init; }

    public int Leechers { get; init; }

    public DateTimeOffset? PublishDate { get; init; }

    public ReleaseKind Kind { get; init; } = ReleaseKind.Unknown;

    public string? ImdbId { get; init; }

    public string? PosterUrl { get; init; }

    /// <summary>
    /// Opaque, stable handle for this release. The browse UI never sees the
    /// real download URL and the download route never accepts one — it only
    /// accepts an Id that this server previously issued. That is what stops
    /// the endpoint from being turned into an open proxy (see README).
    /// </summary>
    public string Id => ComputeId(DownloadUrl ?? MagnetUri ?? DetailsUrl ?? Title);

    private static string ComputeId(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }
}

public sealed class TrackerQuery
{
    public string? SearchTerm { get; init; }

    public ReleaseKind Kind { get; init; } = ReleaseKind.Unknown;

    public int Page { get; init; } = 1;

    public int Limit { get; init; } = 50;

    /// <summary>Optional season/episode for series searches.</summary>
    public int? Season { get; init; }

    public int? Episode { get; init; }
}
