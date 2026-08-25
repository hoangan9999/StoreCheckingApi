namespace StoreChecking.Api.Models;

/// <summary>
/// One line of free-text note attached to a whole month, shown under the calendar grid.
/// <para><c>Period</c> is the first day of the selected month. The calendar cycle runs
/// 26th of the previous month to the 25th, so the cycle 26 Sep → 25 Oct 2026 has
/// Period = 2026-10-01.</para>
/// </summary>
public class WorkMonthNote
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    public DateOnly Period { get; set; }

    public string Content { get; set; } = "";

    /// <summary>Display order within the month.</summary>
    public int Sort { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
