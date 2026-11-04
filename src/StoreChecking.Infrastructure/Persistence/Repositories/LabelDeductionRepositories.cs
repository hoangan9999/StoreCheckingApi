using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Inventory;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Repositories;

public sealed class ProductKindRepository(AppDbContext db) : IProductKindRepository
{
    public async Task<IReadOnlyList<ProductKind>> ListAsync(CancellationToken ct = default) =>
        await db.ProductKinds.OrderBy(x => x.Name).ToListAsync(ct);

    public Task<ProductKind?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.ProductKinds.FirstOrDefaultAsync(x => x.Id == id, ct);

    // Matches the unique index in db/018, which is on lower(btrim(name)). Done in memory
    // because a kind list is a handful of rows and writing the same expression twice —
    // once in SQL, once in LINQ — is how the two drift apart.
    public async Task<ProductKind?> FindByKeyAsync(string nameKey, CancellationToken ct = default)
    {
        var all = await db.ProductKinds.ToListAsync(ct);
        return all.FirstOrDefault(k => LabelText.Key(k.Name) == nameKey);
    }

    public void Add(ProductKind row) => db.ProductKinds.Add(row);
}

public sealed class LabelItemAliasRepository(AppDbContext db) : ILabelItemAliasRepository
{
    public async Task<IReadOnlyList<LabelItemAlias>> ListAsync(CancellationToken ct = default) =>
        await db.LabelItemAliases.OrderBy(x => x.Display).ToListAsync(ct);

    public Task<LabelItemAlias?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.LabelItemAliases.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<LabelItemAlias?> FindByKeyAsync(string sourceName, string variant, CancellationToken ct = default) =>
        db.LabelItemAliases.FirstOrDefaultAsync(x => x.SourceName == sourceName && x.Variant == variant, ct);

    public void Add(LabelItemAlias row) => db.LabelItemAliases.Add(row);
    public void Remove(LabelItemAlias row) => db.LabelItemAliases.Remove(row);
}

public sealed class LabelDeductionRepository(AppDbContext db) : ILabelDeductionRepository
{
    public Task<LabelDeduction?> FindAsync(string orderCode, CancellationToken ct = default) =>
        db.LabelDeductions.FirstOrDefaultAsync(x => x.OrderCode == orderCode, ct);

    public async Task<IReadOnlyList<string>> ListCodesAsync(CancellationToken ct = default) =>
        await db.LabelDeductions.Select(x => x.OrderCode).ToListAsync(ct);

    public async Task<IReadOnlyList<LabelDeduction>> ListByGroupAsync(
        Guid saleGroupId, CancellationToken ct = default) =>
        await db.LabelDeductions.Where(x => x.SaleGroupId == saleGroupId).ToListAsync(ct);

    public void Add(LabelDeduction row) => db.LabelDeductions.Add(row);
    public void Remove(LabelDeduction row) => db.LabelDeductions.Remove(row);
}
