using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Abstractions;

/// <summary>Spending categories, in the order the owner arranged them.</summary>
public interface IExpenseCategoryRepository
{
    Task<IReadOnlyList<ExpenseCategory>> ListAsync(CancellationToken ct = default);
    Task<ExpenseCategory?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Whether any spending still points at this category. Checked before deleting so the
    /// user gets a sentence instead of a raw foreign key violation — the database keeps
    /// refusing it as well, as the last line of defence.
    /// </summary>
    Task<bool> HasExpensesAsync(Guid categoryId, CancellationToken ct = default);

    void Add(ExpenseCategory row);
    void Remove(ExpenseCategory row);
}

/// <summary>Spending transactions.</summary>
public interface IExpenseRepository
{
    /// <summary>One calendar month, newest first. <paramref name="to"/> is exclusive.</summary>
    Task<IReadOnlyList<Expense>> ListRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<Expense?> FindAsync(Guid id, CancellationToken ct = default);
    void Add(Expense row);
    void Remove(Expense row);
}

/// <summary>Income per month.</summary>
public interface IMonthlyIncomeRepository
{
    Task<IReadOnlyList<MonthlyIncome>> ListByYearAsync(int year, CancellationToken ct = default);
    Task<MonthlyIncome?> FindAsync(int year, int month, CancellationToken ct = default);
    void Add(MonthlyIncome row);
}

/// <summary>The two roll-up views. Read only — Postgres does the grouping.</summary>
public interface IExpenseSummaryRepository
{
    Task<IReadOnlyList<MonthCategorySpend>> ByCategoryAsync(int year, int month, CancellationToken ct = default);
    Task<IReadOnlyList<MonthTotal>> ByMonthAsync(int year, CancellationToken ct = default);
}
