using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Repositories;

public sealed class ExpenseCategoryRepository(AppDbContext db) : IExpenseCategoryRepository
{
    public async Task<IReadOnlyList<ExpenseCategory>> ListAsync(CancellationToken ct = default) =>
        await db.ExpenseCategories
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .ToListAsync(ct);

    public Task<ExpenseCategory?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.ExpenseCategories.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> HasExpensesAsync(Guid categoryId, CancellationToken ct = default) =>
        db.Expenses.AnyAsync(x => x.CategoryId == categoryId, ct);

    public void Add(ExpenseCategory row) => db.ExpenseCategories.Add(row);
    public void Remove(ExpenseCategory row) => db.ExpenseCategories.Remove(row);
}

public sealed class ExpenseRepository(AppDbContext db) : IExpenseRepository
{
    // Newest first. CreatedAt breaks ties between two things bought on the same day, and Id
    // breaks ties again for rows written in one transaction, which share created_at exactly.
    public async Task<IReadOnlyList<Expense>> ListRangeAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await db.Expenses
            .Where(x => x.SpentOn >= from && x.SpentOn < to)
            .OrderByDescending(x => x.SpentOn)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public Task<Expense?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(Expense row) => db.Expenses.Add(row);
    public void Remove(Expense row) => db.Expenses.Remove(row);
}

public sealed class MonthlyIncomeRepository(AppDbContext db) : IMonthlyIncomeRepository
{
    public async Task<IReadOnlyList<MonthlyIncome>> ListByYearAsync(int year, CancellationToken ct = default) =>
        await db.MonthlyIncomes
            .Where(x => x.Year == year)
            .OrderBy(x => x.Month)
            .ToListAsync(ct);

    public Task<MonthlyIncome?> FindAsync(int year, int month, CancellationToken ct = default) =>
        db.MonthlyIncomes.FirstOrDefaultAsync(x => x.Year == year && x.Month == month, ct);

    public void Add(MonthlyIncome row) => db.MonthlyIncomes.Add(row);
}

public sealed class ExpenseSummaryRepository(AppDbContext db) : IExpenseSummaryRepository
{
    public async Task<IReadOnlyList<MonthCategorySpend>> ByCategoryAsync(
        int year, int month, CancellationToken ct = default) =>
        await db.MonthCategorySpends
            .Where(x => x.Year == year && x.Month == month)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MonthTotal>> ByMonthAsync(int year, CancellationToken ct = default) =>
        await db.MonthTotals
            .Where(x => x.Year == year)
            .OrderBy(x => x.Month)
            .ToListAsync(ct);
}
