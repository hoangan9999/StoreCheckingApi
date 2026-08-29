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
    /// <summary>
    /// One page of sales history, newest first, with the product and batch named.
    /// <para>Pages count ORDERS, not rows. An order is every row sharing a
    /// <c>SaleGroupId</c>, and a row without one is an order by itself. Paging by row
    /// instead would split an order across a page boundary, and the part that landed on
    /// the first page would show a total for only some of its lines.</para>
    /// <para>The rows come back already in display order: the page's orders in sequence,
    /// lines of an order kept together.</para>
    /// </summary>
    Task<IReadOnlyList<(Sale Sale, string ProductName, string BatchName, int? BatchPriority)>>
        ListPageAsync(int skip, int take, DateTimeOffset? from, DateTimeOffset? to,
                      CancellationToken ct = default);

    /// <summary>
    /// How many orders match and what they add up to, across the WHOLE range rather than
    /// one page. The screen only ever holds a page, so its running totals have to be
    /// counted here or they would shrink to whatever happens to be loaded.
    /// </summary>
    Task<(int Orders, decimal Amount)> SummariseAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

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
