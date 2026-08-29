using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>
/// A spending category — "Ăn uống", "Xăng xe" — with the budgets used to warn when it is
/// being overspent.
/// </summary>
public class ExpenseCategory : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Null means no budget has been set, which is different from a budget of zero.</summary>
    public decimal? MonthlyBudget { get; set; }

    /// <summary>'fixed' or 'variable'. The database enforces it too, with a check constraint.</summary>
    public string Type { get; set; } = "variable";

    /// <summary>An emoji, shown next to the name.</summary>
    public string? Icon { get; set; }

    /// <summary>Null means no per-day warning.</summary>
    public decimal? DailyLimit { get; set; }

    public string? Note { get; set; }

    /// <summary>Display order chosen by hand.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
