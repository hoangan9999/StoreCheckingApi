namespace StoreChecking.Domain.Entities;

using StoreChecking.Domain.Abstractions;

/// <summary>Chặng của một bài viết. Ít chặng hơn video vì không có giọng đọc và ffmpeg.</summary>
public static class PostStatus
{
    public const string Pending = "pending";
    public const string Ready   = "ready";
    public const string Error   = "error";
}

/// <summary>
/// One Fanpage post the daily job wrote from a single picture in the album.
///
/// <para>Sits beside <see cref="GeneratedVideo"/> rather than sharing a table with it: a
/// post is one picture and a caption with no voice, no duration and no file on disk, so
/// half the video columns would sit empty forever.</para>
/// </summary>
public class GeneratedPost : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    /// <summary>The picture this is about. One, not many — that is the whole difference.</summary>
    public Guid ImageId { get; set; }

    public string Title { get; set; } = "";

    /// <summary>What the AI wrote. The order link and the invitation to message are added
    /// when posting, so this stays reusable if either of those changes.</summary>
    public string Content { get; set; } = "";

    public string Status { get; set; } = PostStatus.Pending;
    public string? Error { get; set; }

    /// <summary>Ngày của mẻ, theo giờ Việt Nam.</summary>
    public DateOnly BatchDay { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PostedAt { get; set; }
    public string? FbPostId { get; set; }

    /// <summary>Vì sao đăng hỏng. Tách khỏi <see cref="Error"/>: nội dung vẫn viết xong.</summary>
    public string? PostError { get; set; }
}
