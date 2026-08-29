using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Configurations;

public sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> e)
    {
        e.ToTable("expense_categories");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Name).HasColumnName("name");
        e.Property(x => x.MonthlyBudget).HasColumnName("monthly_budget").HasColumnType("numeric(14,2)");
        e.Property(x => x.Type).HasColumnName("type").HasDefaultValue("variable");
        e.Property(x => x.Icon).HasColumnName("icon");
        e.Property(x => x.DailyLimit).HasColumnName("daily_limit").HasColumnType("numeric(14,2)");
        e.Property(x => x.Note).HasColumnName("note");
        e.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.SortOrder });
    }
}

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> e)
    {
        e.ToTable("expenses");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.CategoryId).HasColumnName("category_id");
        e.Property(x => x.SpentOn).HasColumnName("spent_on");
        e.Property(x => x.Description).HasColumnName("description");
        e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)");
        e.Property(x => x.Note).HasColumnName("note");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.SpentOn });

        // No navigation property to ExpenseCategory on purpose. A navigation invites Include,
        // and an Include on a filtered entity is one more place where the owner filter has to
        // be reasoned about; the category is fetched through its own repository instead.
    }
}

public sealed class MonthlyIncomeConfiguration : IEntityTypeConfiguration<MonthlyIncome>
{
    public void Configure(EntityTypeBuilder<MonthlyIncome> e)
    {
        e.ToTable("monthly_income");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Year).HasColumnName("year");
        e.Property(x => x.Month).HasColumnName("month");
        e.Property(x => x.Income).HasColumnName("income").HasColumnType("numeric(14,2)").HasDefaultValue(0m);
        e.Property(x => x.Note).HasColumnName("note");
        e.HasIndex(x => new { x.UserId, x.Year, x.Month }).IsUnique();
    }
}

// The two roll-ups are VIEWS, so they are keyless: there is no row identity to track and
// nothing to write back. Mapping them keeps the grouping in Postgres, where it is written
// once, instead of repeating it in every screen that shows a total.

public sealed class MonthCategorySpendConfiguration : IEntityTypeConfiguration<MonthCategorySpend>
{
    public void Configure(EntityTypeBuilder<MonthCategorySpend> e)
    {
        e.HasNoKey().ToView("v_expense_month_category");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.CategoryId).HasColumnName("category_id");
        e.Property(x => x.Year).HasColumnName("year");
        e.Property(x => x.Month).HasColumnName("month");
        e.Property(x => x.Spent).HasColumnName("spent");
        e.Property(x => x.TxCount).HasColumnName("tx_count");
    }
}

public sealed class MonthTotalConfiguration : IEntityTypeConfiguration<MonthTotal>
{
    public void Configure(EntityTypeBuilder<MonthTotal> e)
    {
        e.HasNoKey().ToView("v_expense_month_total");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Year).HasColumnName("year");
        e.Property(x => x.Month).HasColumnName("month");
        e.Property(x => x.Spent).HasColumnName("spent");
    }
}
