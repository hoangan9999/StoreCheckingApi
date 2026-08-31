using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>
/// A quick note kept for copying rather than retyping — bank details, a message template,
/// a size chart.
/// <para><c>Content</c> is stored exactly as written, whitespace included: it is meant to
/// be copied to the clipboard verbatim, so trimming it would corrupt templates that rely
/// on their own layout.</para>
/// </summary>
public class Note : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    /// <summary>Optional heading. Null when the note is just a blob of text.</summary>
    public string? Title { get; set; }

    public string Content { get; set; } = "";

    /// <summary>
    /// Ảnh đính kèm — chỉ tên file, ảnh nằm trên đĩa trong thư mục `notes`.
    /// <para>Tách hẳn khỏi kho ảnh của video tự sinh: để chung thì ảnh chụp màn hình hay
    /// ảnh hoá đơn sẽ lọt vào video bán xe.</para>
    /// </summary>
    public string[] Images { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
