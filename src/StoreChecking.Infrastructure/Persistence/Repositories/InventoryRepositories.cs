using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Repositories;

public sealed class BatchRepository(AppDbContext db) : IBatchRepository
{
    public async Task<IReadOnlyList<Batch>> ListAsync(CancellationToken ct = default) =>
        await db.Batches.ToListAsync(ct);

    public Task<Batch?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.Batches.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<BatchSummary>> SummariesAsync(CancellationToken ct = default) =>
        await db.BatchSummaries.ToListAsync(ct);

    public Task<BatchSummary?> SummaryAsync(Guid id, CancellationToken ct = default) =>
        db.BatchSummaries.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(Batch row) => db.Batches.Add(row);
    public void Remove(Batch row) => db.Batches.Remove(row);
}

public sealed class ProductRepository(AppDbContext db) : IProductRepository
{
    public Task<Product?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);

    // Oldest first: the order products were typed in when the batch was created, which is
    // the order the batch screen shows them in.
    public async Task<IReadOnlyList<ProductStock>> StockByBatchAsync(Guid batchId, CancellationToken ct = default) =>
        await db.ProductStocks
            .Where(x => x.BatchId == batchId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProductStock>> InStockAsync(CancellationToken ct = default) =>
        await db.ProductStocks
            .Where(x => x.Remaining > 0)
            .ToListAsync(ct);

    public void Add(Product row) => db.Products.Add(row);
    public void AddRange(IEnumerable<Product> rows) => db.Products.AddRange(rows);
    public void Remove(Product row) => db.Products.Remove(row);
}

public sealed class SaleRepository(AppDbContext db) : ISaleRepository
{
    // A join rather than a navigation property. Supabase did this with embedded selects
    // (`products(name), batches(name, priority)`); here both sides run under the owner
    // filter, so a sale can never pick up the name of somebody else's product.
    public async Task<IReadOnlyList<(Sale Sale, string ProductName, string BatchName, int? BatchPriority)>>
        ListAllAsync(int take, CancellationToken ct = default)
    {
        var rows = await db.Sales
            .OrderByDescending(x => x.SoldAt).ThenByDescending(x => x.Id)
            .Take(take)
            .Join(db.Products, s => s.ProductId, p => p.Id, (s, p) => new { Sale = s, Product = p })
            .Join(db.Batches, sp => sp.Sale.BatchId, b => b.Id,
                  (sp, b) => new { sp.Sale, ProductName = sp.Product.Name, BatchName = b.Name, b.Priority })
            .ToListAsync(ct);

        return rows
            .Select(r => (r.Sale, r.ProductName, r.BatchName, r.Priority))
            .ToList();
    }

    public async Task<IReadOnlyList<Sale>> ListByBatchAsync(Guid batchId, CancellationToken ct = default) =>
        await db.Sales
            .Where(x => x.BatchId == batchId)
            .OrderByDescending(x => x.SoldAt).ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public Task<Sale?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.Sales.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Sale>> ListByGroupAsync(Guid groupId, CancellationToken ct = default) =>
        await db.Sales.Where(x => x.SaleGroupId == groupId).ToListAsync(ct);

    /// <summary>
    /// Bought in, less sold, less damaged — the same arithmetic as the check_stock trigger,
    /// so the message the user gets and the rule the database enforces agree.
    /// </summary>
    public async Task<long> AvailableAsync(
        Guid productId, Guid? ignoreSaleId = null, CancellationToken ct = default)
    {
        var quantity = await db.Products
            .Where(x => x.Id == productId)
            .Select(x => (int?)x.Quantity)
            .FirstOrDefaultAsync(ct);

        if (quantity is null) return 0;

        var sold = await db.Sales
            .Where(x => x.ProductId == productId && (ignoreSaleId == null || x.Id != ignoreSaleId))
            .SumAsync(x => (long?)x.Quantity, ct) ?? 0;

        var damaged = await db.ProductDamages
            .Where(x => x.ProductId == productId)
            .SumAsync(x => (long?)x.Quantity, ct) ?? 0;

        return quantity.Value - sold - damaged;
    }

    public void Add(Sale row) => db.Sales.Add(row);
    public void AddRange(IEnumerable<Sale> rows) => db.Sales.AddRange(rows);
    public void Remove(Sale row) => db.Sales.Remove(row);
    public void RemoveRange(IEnumerable<Sale> rows) => db.Sales.RemoveRange(rows);
}

public sealed class ProductDamageRepository(AppDbContext db) : IProductDamageRepository
{
    public async Task<IReadOnlyList<ProductDamage>> ListByBatchAsync(Guid batchId, CancellationToken ct = default) =>
        await db.ProductDamages
            .Where(x => x.BatchId == batchId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public Task<ProductDamage?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.ProductDamages.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(ProductDamage row) => db.ProductDamages.Add(row);
    public void Remove(ProductDamage row) => db.ProductDamages.Remove(row);
}
