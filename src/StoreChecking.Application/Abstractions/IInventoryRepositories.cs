using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Abstractions;

/// <summary>Batches, and the roll-up view over them.</summary>
public interface IBatchRepository
{
    Task<IReadOnlyList<Batch>> ListAsync(CancellationToken ct = default);
    Task<Batch?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>The <c>batch_summary</c> view. Has no <c>priority</c> — read that from the table.</summary>
    Task<IReadOnlyList<BatchSummary>> SummariesAsync(CancellationToken ct = default);

    Task<BatchSummary?> SummaryAsync(Guid id, CancellationToken ct = default);

    void Add(Batch row);
    void Remove(Batch row);
}

/// <summary>Products, and the per-product stock view.</summary>
public interface IProductRepository
{
    Task<Product?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Stock for one batch, oldest first — the order products were added in.</summary>
    Task<IReadOnlyList<ProductStock>> StockByBatchAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Everything still in stock anywhere, for picking when selling.</summary>
    Task<IReadOnlyList<ProductStock>> InStockAsync(CancellationToken ct = default);

    void Add(Product row);
    void AddRange(IEnumerable<Product> rows);
    void Remove(Product row);
}

/// <summary>Sale lines.</summary>
public interface ISaleRepository
{
    /// <summary>Every sale, newest first, with the product and batch named.</summary>
    Task<IReadOnlyList<(Sale Sale, string ProductName, string BatchName, int? BatchPriority)>>
        ListAllAsync(int take, CancellationToken ct = default);

    Task<IReadOnlyList<Sale>> ListByBatchAsync(Guid batchId, CancellationToken ct = default);
    Task<Sale?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> ListByGroupAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// How many units of a product are still available: bought in, less sold, less damaged.
    /// <para><paramref name="ignoreSaleId"/> leaves one sale line out of the sum, which is
    /// what makes the figure right when editing that very line.</para>
    /// </summary>
    Task<long> AvailableAsync(Guid productId, Guid? ignoreSaleId = null, CancellationToken ct = default);

    void Add(Sale row);
    void AddRange(IEnumerable<Sale> rows);
    void Remove(Sale row);
    void RemoveRange(IEnumerable<Sale> rows);
}

/// <summary>Damage write-offs.</summary>
public interface IProductDamageRepository
{
    Task<IReadOnlyList<ProductDamage>> ListByBatchAsync(Guid batchId, CancellationToken ct = default);
    Task<ProductDamage?> FindAsync(Guid id, CancellationToken ct = default);
    void Add(ProductDamage row);
    void Remove(ProductDamage row);
}
