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
    /// <summary>One page of a date range, newest first. <paramref name="to"/> is exclusive.</summary>
    Task<IReadOnlyList<Expense>> ListPageAsync(
        DateOnly from, DateOnly to, Guid? categoryId, DateOnly? on, int skip, int take,
        CancellationToken ct = default);

    /// <summary>
    /// How many transactions match and what they add up to, across the whole range rather
    /// than one page — the month total on screen must not shrink to the rows loaded so far.
    /// </summary>
    Task<(int Count, decimal Amount)> SummariseAsync(
        DateOnly from, DateOnly to, Guid? categoryId, DateOnly? on, CancellationToken ct = default);

    /// <summary>
    /// Spend per category per day over a range.
    /// <para>This is what the daily-limit warning runs on. It used to be added up in the
    /// browser from the month's full list, which stops working the moment the list is paged
    /// — so the sum moves to where all the rows still are.</para>
    /// </summary>
    Task<IReadOnlyList<(Guid CategoryId, DateOnly SpentOn, decimal Total)>> DayTotalsAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

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
