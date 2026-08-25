namespace StoreChecking.Api.Models;

/// <summary>
/// Một ô ngày trong lịch làm. Chỉ ô có ghi chú hoặc có màu mới tồn tại dòng trong DB.
/// <para><c>Color</c> là KHOÁ màu ('vang', 'luc'…), không phải mã hex — để ô tô màu
/// hợp cả giao diện sáng lẫn tối bên phía Angular.</para>
/// </summary>
public class WorkDay
{
    public Guid Id { get; set; }

    /// <summary>Chủ sở hữu. Lấy từ claim `sub` của token, KHÔNG nhận từ client.</summary>
    public Guid UserId { get; set; }

    /// <summary>Ngày cụ thể (chỉ phần ngày, không giờ).</summary>
    public DateOnly Day { get; set; }

    public string Note { get; set; } = "";

    public string? Color { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
