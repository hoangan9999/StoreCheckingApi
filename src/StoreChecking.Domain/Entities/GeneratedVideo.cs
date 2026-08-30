namespace StoreChecking.Domain.Entities;

using StoreChecking.Domain.Abstractions;

/// <summary>Where a video got to. Ordered as the work happens.</summary>
public static class VideoStatus
{
    public const string Pending   = "pending";
    public const string Writing   = "writing";     // AI đang viết kịch bản
    public const string Voicing   = "voicing";     // giọng Adam đang đọc
    public const string Rendering = "rendering";   // ffmpeg đang ghép
    public const string Ready     = "ready";
    public const string Error     = "error";
}

/// <summary>
/// One video the daily job built from pictures in the album.
///
/// <para><see cref="Status"/> names the stage rather than just success or failure, because
/// the three ways this breaks — the AI writing, the voice, ffmpeg — need completely
/// different fixes, and "error" alone would hide which one it was.</para>
/// </summary>
public class GeneratedVideo : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    /// <summary>File on disk. Null until rendering finishes.</summary>
    public string? Filename { get; set; }

    public string Title { get; set; } = "";

    /// <summary>What the AI wrote. Also exactly what the voice reads, so it is worth keeping.</summary>
    public string Script { get; set; } = "";

    public decimal? DurationSec { get; set; }
    public long? Bytes { get; set; }

    public string Status { get; set; } = VideoStatus.Pending;
    public string? Error { get; set; }

    /// <summary>Pictures used, so a video can be traced back to where it came from.</summary>
    public Guid[] ImageIds { get; set; } = [];

    /// <summary>
    /// The day this batch belongs to, in Vietnam time.
    /// <para>A date rather than a timestamp so "have five been made today?" cannot be
    /// answered differently depending on the server's timezone.</para>
    /// </summary>
    public DateOnly BatchDay { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// When it was downloaded, or null if it never was.
    /// <para>Five new videos a day: without this there is no telling after a few days which
    /// ones were already taken. A timestamp rather than a flag answers the same question and
    /// also says when.</para>
    /// </summary>
    public DateTimeOffset? DownloadedAt { get; set; }
}
