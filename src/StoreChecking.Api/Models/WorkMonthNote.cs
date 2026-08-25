namespace StoreChecking.Api.Models;

/// <summary>
/// Một dòng ghi chú chung của cả tháng (hiện dưới lưới lịch).
/// <para><c>Period</c> = ngày 1 của tháng đang chọn. Ví dụ chu kỳ 26/9 → 25/10/2026
/// thì Period = 2026-10-01.</para>
/// </summary>
public class WorkMonthNote
{
    public Guid Id { get; set; }

    /// <summary>Chủ sở hữu. Lấy từ claim `sub` của token, KHÔNG nhận từ client.</summary>
    public Guid UserId { get; set; }

    public DateOnly Period { get; set; }

    public string Content { get; set; } = "";

    /// <summary>Thứ tự hiển thị trong tháng.</summary>
    public int Sort { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
