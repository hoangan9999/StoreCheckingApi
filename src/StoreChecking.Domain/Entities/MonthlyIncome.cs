using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>
/// Income recorded for one month, so spending can be shown against what came in.
/// One row per user per month — the database has a unique index saying so.
/// </summary>
public class MonthlyIncome : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    public int Year { get; set; }

    /// <summary>1 to 12. The database checks this too.</summary>
    public int Month { get; set; }

    public decimal Income { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Số còn lại đã được người dùng tự làm tròn, dùng thay cho số tính ra khi cộng cả năm.
    /// <para>NULL nghĩa là chưa chốt — bên gọi lấy số tính ra. KHÔNG dùng 0 làm mặc định:
    /// 0 là một con số chốt hợp lệ, lẫn hai thứ đó thì mọi tháng chưa nhập sẽ bị cộng
    /// thành 0 và tổng cả năm sai hẳn.</para>
    /// </summary>
    public decimal? RoundedSavings { get; set; }
}
