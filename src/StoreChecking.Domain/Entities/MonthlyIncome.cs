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
}
