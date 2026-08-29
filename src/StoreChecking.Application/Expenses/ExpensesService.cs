using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Common;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Expenses;

/// <summary>
/// Spending: the categories, the transactions, the income they are measured against, and
/// the two roll-ups the charts read.
/// </summary>
public sealed class ExpensesService(
    IExpenseCategoryRepository categories,
    IExpenseRepository expenses,
    IMonthlyIncomeRepository incomes,
    IExpenseSummaryRepository summaries,
    ICurrentUser user,
    IUnitOfWork uow)
{
    /// <summary>Date format shared with the Angular client, same as the work calendar.</summary>
    public const string DayFormat = "yyyy-MM-dd";

    /// <summary>The only two values the database's check constraint allows.</summary>
    private static readonly string[] Types = ["fixed", "variable"];

    private static ExpenseCategoryDto ToDto(ExpenseCategory r) =>
        new(r.Id, r.Name, r.MonthlyBudget, r.Type, r.Icon, r.DailyLimit, r.Note, r.SortOrder);

    private static ExpenseDto ToDto(Expense r) =>
        new(r.Id, r.CategoryId, r.SpentOn.ToString(DayFormat), r.Description, r.Amount, r.Note, r.CreatedAt);

    private static MonthlyIncomeDto ToDto(MonthlyIncome r) => new(r.Id, r.Year, r.Month, r.Income, r.Note);

    // ---------- Categories ----------

    public async Task<IReadOnlyList<ExpenseCategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var rows = await categories.ListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ExpenseCategoryDto> AddCategoryAsync(SaveCategoryRequest body, CancellationToken ct = default)
    {
        var row = new ExpenseCategory { UserId = user.Id };
        Apply(row, body);

        // A new category goes to the end unless the client says where it belongs.
        if (body.SortOrder is null)
        {
            var existing = await categories.ListAsync(ct);
            row.SortOrder = existing.Count + 1;
        }

        categories.Add(row);
        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <returns><c>null</c> when no such category belongs to the current user.</returns>
    public async Task<ExpenseCategoryDto?> UpdateCategoryAsync(
        Guid id, SaveCategoryRequest body, CancellationToken ct = default)
    {
        var row = await categories.FindAsync(id, ct);
        if (row is null) return null;

        Apply(row, body);
        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    private static void Apply(ExpenseCategory row, SaveCategoryRequest body)
    {
        var name = (body.Name ?? "").Trim();
        if (name.Length == 0) throw new ValidationException("Thiếu tên danh mục.");

        var type = string.IsNullOrWhiteSpace(body.Type) ? "variable" : body.Type.Trim().ToLowerInvariant();
        if (!Types.Contains(type)) throw new ValidationException("Loại danh mục phải là 'fixed' hoặc 'variable'.");

        if (body.MonthlyBudget is < 0) throw new ValidationException("Ngân sách tháng không được âm.");
        if (body.DailyLimit is < 0) throw new ValidationException("Hạn mức ngày không được âm.");

        row.Name = name;
        row.MonthlyBudget = body.MonthlyBudget;
        row.Type = type;
        row.Icon = string.IsNullOrWhiteSpace(body.Icon) ? null : body.Icon.Trim();
        row.DailyLimit = body.DailyLimit;
        row.Note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();
        if (body.SortOrder is { } s) row.SortOrder = s;
    }

    /// <returns><c>false</c> when no such category belongs to the current user.</returns>
    public async Task<bool> DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var row = await categories.FindAsync(id, ct);
        if (row is null) return false;

        // Checked here so the user reads a sentence rather than a Postgres foreign key
        // error. The `on delete restrict` in the schema still stands behind it: deleting a
        // category that has spending would erase the record of where that money went.
        if (await categories.HasExpensesAsync(id, ct))
            throw new ValidationException("Danh mục còn giao dịch, không xoá được. Chuyển các giao dịch sang danh mục khác trước.");

        categories.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    // ---------- Transactions ----------

    /// <summary>Everything spent in one calendar month, newest first.</summary>
    public async Task<IReadOnlyList<ExpenseDto>> ListAsync(int year, int month, CancellationToken ct = default)
    {
        GuardMonth(year, month);

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1);          // exclusive, so December rolls into January cleanly

        var rows = await expenses.ListRangeAsync(from, to, ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ExpenseDto> AddAsync(SaveExpenseRequest body, CancellationToken ct = default)
    {
        var row = new Expense { UserId = user.Id };
        await ApplyAsync(row, body, ct);

        expenses.Add(row);
        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <returns><c>null</c> when no such expense belongs to the current user.</returns>
    public async Task<ExpenseDto?> UpdateAsync(Guid id, SaveExpenseRequest body, CancellationToken ct = default)
    {
        var row = await expenses.FindAsync(id, ct);
        if (row is null) return null;

        await ApplyAsync(row, body, ct);
        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    private async Task ApplyAsync(Expense row, SaveExpenseRequest body, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(body.SpentOn, DayFormat, out var day))
            throw new ValidationException("Ngày chi phải dạng YYYY-MM-DD.");

        if (body.Amount < 0) throw new ValidationException("Số tiền không được âm.");

        // Checked rather than left to the foreign key, and checked through the repository so
        // it runs under the owner filter: pointing an expense at someone else's category
        // would otherwise be a way to learn that the category exists.
        var category = await categories.FindAsync(body.CategoryId, ct);
        if (category is null) throw new ValidationException("Danh mục không tồn tại.");

        row.CategoryId = category.Id;
        row.SpentOn = day;
        row.Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim();
        row.Amount = body.Amount;
        row.Note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();
    }

    /// <returns><c>false</c> when no such expense belongs to the current user.</returns>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await expenses.FindAsync(id, ct);
        if (row is null) return false;

        expenses.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    // ---------- Monthly income ----------

    public async Task<IReadOnlyList<MonthlyIncomeDto>> ListIncomeAsync(int year, CancellationToken ct = default)
    {
        var rows = await incomes.ListByYearAsync(year, ct);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// Sets the income for one month, creating the row if that month has none.
    /// <para>The client calls this whether or not a value exists yet, so it upserts rather
    /// than distinguishing the two — matching the unique index on (user, year, month).</para>
    /// </summary>
    public async Task<MonthlyIncomeDto> SetIncomeAsync(SetIncomeRequest body, CancellationToken ct = default)
    {
        GuardMonth(body.Year, body.Month);
        if (body.Income < 0) throw new ValidationException("Thu nhập không được âm.");

        var row = await incomes.FindAsync(body.Year, body.Month, ct);
        if (row is null)
        {
            row = new MonthlyIncome { UserId = user.Id, Year = body.Year, Month = body.Month };
            incomes.Add(row);
        }

        row.Income = body.Income;
        row.Note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();

        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    // ---------- Roll-ups ----------

    public async Task<IReadOnlyList<MonthCategorySpendDto>> SpendByCategoryAsync(
        int year, int month, CancellationToken ct = default)
    {
        GuardMonth(year, month);

        var rows = await summaries.ByCategoryAsync(year, month, ct);
        return rows.Select(r => new MonthCategorySpendDto(r.CategoryId, r.Year, r.Month, r.Spent, r.TxCount)).ToList();
    }

    public async Task<IReadOnlyList<MonthTotalDto>> TotalsByMonthAsync(int year, CancellationToken ct = default)
    {
        var rows = await summaries.ByMonthAsync(year, ct);
        return rows.Select(r => new MonthTotalDto(r.Year, r.Month, r.Spent)).ToList();
    }

    private static void GuardMonth(int year, int month)
    {
        if (month is < 1 or > 12) throw new ValidationException("Tháng phải từ 1 đến 12.");
        if (year is < 2000 or > 2999) throw new ValidationException("Năm không hợp lệ.");
    }
}
