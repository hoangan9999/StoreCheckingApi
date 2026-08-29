using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

// Read-only rows from the two roll-up views. Postgres does the grouping, which keeps the
// arithmetic in one place rather than repeating it in every screen that shows a total.
//
// Both carry user_id, which is what lets the same owner filter apply to them as to a real
// table — and the guard tests insist on exactly that.

/// <summary>Spending per category per month, from the view <c>v_expense_month_category</c>.</summary>
public class MonthCategorySpend : IOwnedByUser
{
    public Guid UserId { get; set; }
    public Guid CategoryId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Spent { get; set; }

    /// <summary>How many transactions made up that total. <c>count(*)</c> is a bigint.</summary>
    public long TxCount { get; set; }
}

/// <summary>Total spending per month across every category, from <c>v_expense_month_total</c>.</summary>
public class MonthTotal : IOwnedByUser
{
    public Guid UserId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Spent { get; set; }
}
