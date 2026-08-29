namespace StoreChecking.Application.Expenses;

// ---------- Categories ----------

/// <summary>A spending category as returned to the client.</summary>
public record ExpenseCategoryDto(
    Guid Id, string Name, decimal? MonthlyBudget, string Type,
    string? Icon, decimal? DailyLimit, string? Note, int SortOrder);

/// <summary>
/// Create or replace a category. A full replacement, not a patch: the client edits it in a
/// dialog that hands back every field, so anything omitted really is meant to be cleared.
/// </summary>
public record SaveCategoryRequest(
    string Name, decimal? MonthlyBudget, string? Type,
    string? Icon, decimal? DailyLimit, string? Note, int? SortOrder);

// ---------- Transactions ----------

/// <summary>One expense as returned to the client. <c>SpentOn</c> is 'YYYY-MM-DD'.</summary>
public record ExpenseDto(
    Guid Id, Guid CategoryId, string SpentOn, string? Description,
    decimal Amount, string? Note, DateTimeOffset CreatedAt);

/// <summary>Create or replace one expense. <c>SpentOn</c> is 'YYYY-MM-DD'.</summary>
public record SaveExpenseRequest(
    Guid CategoryId, string SpentOn, string? Description, decimal Amount, string? Note);

// ---------- Monthly income ----------

/// <summary>Income recorded for one month.</summary>
public record MonthlyIncomeDto(Guid Id, int Year, int Month, decimal Income, string? Note);

/// <summary>Set the income for one month. Creates the row if the month has none yet.</summary>
public record SetIncomeRequest(int Year, int Month, decimal Income, string? Note);

// ---------- Roll-ups ----------

/// <summary>Spending in one category in one month.</summary>
public record MonthCategorySpendDto(Guid CategoryId, int Year, int Month, decimal Spent, long TxCount);

/// <summary>
/// One page of transactions.
/// <para><c>Total</c> and <c>TotalAmount</c> cover the whole filtered month, not the page,
/// so the count and the month total stay right no matter how far down the list has been
/// scrolled.</para>
/// </summary>
public record ExpensePageDto(
    int Total, decimal TotalAmount, int Limit, int Offset, IReadOnlyList<ExpenseDto> Items);

/// <summary>
/// What one category was spent on in one day.
/// <para>Feeds the daily-limit warning. The browser used to add this up from the month's
/// full list, which no longer works once the list is paged.</para>
/// </summary>
public record DayCategorySpendDto(Guid CategoryId, DateOnly SpentOn, decimal Total);

/// <summary>Total spending in one month.</summary>
public record MonthTotalDto(int Year, int Month, decimal Spent);
