using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Common;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Inventory;

/// <summary>
/// Batches, the products in them, what was sold, and what was written off as damaged.
/// <para>The most valuable data in the application, which is why it migrated last.</para>
/// <para>Stock is checked HERE and again by a trigger in the database. Not redundancy for
/// its own sake: the check here produces a message worth reading, and the trigger holds
/// when two sales of the last item arrive at the same moment and both pass their check.
/// </para>
/// </summary>
public sealed class InventoryService(
    IBatchRepository batches,
    IProductRepository products,
    ISaleRepository sales,
    IProductDamageRepository damages,
    ICurrentUser user,
    IUnitOfWork uow)
{
    public const string DayFormat = "yyyy-MM-dd";

    /// <summary>Upper bound on the global sale history. The client asks for 300.</summary>
    private const int MaxSaleHistory = 5_000;

    /// <summary>Orders per page when the client does not ask for a size.</summary>
    private const int DefaultSalePage = 20;

    /// <summary>Unranked batches sort after every ranked one.</summary>
    private const int Unranked = int.MaxValue;

    // ---------- Batches ----------

    /// <summary>
    /// Every batch with its roll-up, ordered the way the selling screen wants them:
    /// by priority, then newest first.
    /// </summary>
    public async Task<IReadOnlyList<BatchSummaryDto>> ListBatchesAsync(CancellationToken ct = default)
    {
        // Two reads because batch_summary has no priority column. Merged here rather than in
        // the browser, which is what the client used to do.
        var summaries = await batches.SummariesAsync(ct);
        var priorities = (await batches.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Priority);

        return summaries
            .Select(s => ToDto(s, priorities.GetValueOrDefault(s.Id)))
            .OrderBy(d => d.Priority ?? Unranked)
            .ThenByDescending(d => d.CreatedAt)
            .ToList();
    }

    /// <returns><c>null</c> when no such batch belongs to the current user.</returns>
    public async Task<BatchSummaryDto?> GetBatchAsync(Guid id, CancellationToken ct = default)
    {
        var summary = await batches.SummaryAsync(id, ct);
        if (summary is null) return null;

        var batch = await batches.FindAsync(id, ct);
        return ToDto(summary, batch?.Priority);
    }

    public async Task<BatchSummaryDto> CreateBatchAsync(CreateBatchRequest body, CancellationToken ct = default)
    {
        var name = Required(body.Name, "Thiếu tên lô.");
        var importDate = ParseDay(body.ImportDate, "Ngày nhập phải dạng YYYY-MM-DD.");
        if (body.TotalCost < 0) throw new ValidationException("Giá trị nhập không được âm.");

        var batch = new Batch
        {
            // Generated here rather than by the database, so the products can point at it
            // within the same SaveChanges. Waiting for gen_random_uuid() would mean writing
            // the batch first and the products second — two transactions, and a batch that
            // half exists is worse than one that failed outright.
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = name,
            ImportDate = importDate,
            TotalCost = body.TotalCost,
            Note = Clean(body.Note),
        };
        batches.Add(batch);

        var rows = (body.Products ?? [])
            .Select(p => new Product
            {
                UserId = user.Id,
                BatchId = batch.Id,
                Name = Required(p.Name, "Sản phẩm thiếu tên."),
                Quantity = Guard(p.Quantity ?? 0, "Số lượng không được âm."),
                SellPrice = GuardMoney(p.SellPrice ?? 0, "Giá bán không được âm."),
            })
            .ToList();

        if (rows.Count > 0) products.AddRange(rows);

        await uow.SaveChangesAsync(ct);
        return (await GetBatchAsync(batch.Id, ct))!;
    }

    /// <returns><c>null</c> when no such batch belongs to the current user.</returns>
    public async Task<BatchSummaryDto?> UpdateBatchAsync(
        Guid id, UpdateBatchRequest body, CancellationToken ct = default)
    {
        var batch = await batches.FindAsync(id, ct);
        if (batch is null) return null;

        if (body.TotalCost < 0) throw new ValidationException("Giá trị nhập không được âm.");
        if (body.Priority is < 1) throw new ValidationException("Ưu tiên phải từ 1 trở lên.");

        batch.Name = Required(body.Name, "Thiếu tên lô.");
        batch.ImportDate = ParseDay(body.ImportDate, "Ngày nhập phải dạng YYYY-MM-DD.");
        batch.TotalCost = body.TotalCost;
        batch.Note = Clean(body.Note);
        batch.Priority = body.Priority;

        await uow.SaveChangesAsync(ct);
        return await GetBatchAsync(id, ct);
    }

    /// <returns><c>false</c> when no such batch belongs to the current user.</returns>
    public async Task<bool> DeleteBatchAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await batches.FindAsync(id, ct);
        if (batch is null) return false;

        // The products, sales and damages under it go too — `on delete cascade` in the
        // schema. Deleting a batch really does mean deleting its whole history.
        batches.Remove(batch);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Reorders several batches at once, for drag-and-drop.</summary>
    /// <returns>How many were actually changed. Ids that are not the caller's are ignored.</returns>
    public async Task<int> SetPrioritiesAsync(SetPrioritiesRequest body, CancellationToken ct = default)
    {
        var items = body.Items ?? [];
        if (items.Count == 0) return 0;
        if (items.Any(i => i.Priority < 1)) throw new ValidationException("Ưu tiên phải từ 1 trở lên.");

        // One read of the caller's batches rather than one lookup per item, and it doubles
        // as the ownership check: an id that is not here simply never gets touched.
        var mine = (await batches.ListAsync(ct)).ToDictionary(b => b.Id);
        var changed = 0;

        foreach (var item in items)
        {
            if (!mine.TryGetValue(item.Id, out var batch)) continue;
            batch.Priority = item.Priority;
            changed++;
        }

        if (changed > 0) await uow.SaveChangesAsync(ct);
        return changed;
    }

    // ---------- Products ----------

    /// <returns><c>null</c> when no such batch belongs to the current user.</returns>
    public async Task<IReadOnlyList<ProductStockDto>?> ListProductsAsync(Guid batchId, CancellationToken ct = default)
    {
        if (await batches.FindAsync(batchId, ct) is null) return null;

        var rows = await products.StockByBatchAsync(batchId, ct);
        return rows.Select(ToDto).ToList();
    }

    /// <returns><c>null</c> when no such batch belongs to the current user.</returns>
    public async Task<ProductStockDto?> AddProductAsync(
        Guid batchId, SaveProductRequest body, CancellationToken ct = default)
    {
        var batch = await batches.FindAsync(batchId, ct);
        if (batch is null) return null;

        var row = new Product
        {
            UserId = user.Id,
            BatchId = batch.Id,
            Name = Required(body.Name, "Thiếu tên sản phẩm."),
            Quantity = Guard(body.Quantity, "Số lượng không được âm."),
            SellPrice = GuardMoney(body.SellPrice, "Giá bán không được âm."),
        };
        products.Add(row);
        await uow.SaveChangesAsync(ct);

        return (await FindStockAsync(row.Id, batch.Id, ct))!;
    }

    /// <returns><c>null</c> when no such product belongs to the current user.</returns>
    public async Task<ProductStockDto?> UpdateProductAsync(
        Guid id, SaveProductRequest body, CancellationToken ct = default)
    {
        var row = await products.FindAsync(id, ct);
        if (row is null) return null;

        var quantity = Guard(body.Quantity, "Số lượng không được âm.");

        // Lowering the quantity below what has already left the shelf would make the stock
        // figure negative. The trigger does not cover this — it watches sales and damages,
        // not the product row — so this check is the only thing standing in the way.
        var gone = await sales.AvailableAsync(id, ct: ct);
        var committed = row.Quantity - gone;
        if (quantity < committed)
            throw new ValidationException($"Đã bán hoặc hư {committed}, không thể giảm số lượng nhập xuống {quantity}.");

        row.Name = Required(body.Name, "Thiếu tên sản phẩm.");
        row.Quantity = quantity;
        row.SellPrice = GuardMoney(body.SellPrice, "Giá bán không được âm.");

        await uow.SaveChangesAsync(ct);
        return await FindStockAsync(row.Id, row.BatchId, ct);
    }

    /// <returns><c>false</c> when no such product belongs to the current user.</returns>
    public async Task<bool> DeleteProductAsync(Guid id, CancellationToken ct = default)
    {
        var row = await products.FindAsync(id, ct);
        if (row is null) return false;

        // Its sales and damages cascade away with it, which is how Supabase behaved too.
        products.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Everything still in stock, ordered by batch priority then by name.</summary>
    public async Task<IReadOnlyList<StockItemDto>> ListStockAsync(CancellationToken ct = default)
    {
        var stock = await products.InStockAsync(ct);
        var byBatch = (await batches.ListAsync(ct)).ToDictionary(b => b.Id);

        return stock
            .Select(p =>
            {
                var batch = byBatch.GetValueOrDefault(p.BatchId);
                return new StockItemDto(
                    p.Id, p.Name, p.BatchId,
                    batch?.Name ?? "(lô?)", batch?.Priority,
                    p.SellPrice, p.Remaining);
            })
            .OrderBy(x => x.BatchPriority ?? Unranked)
            .ThenBy(x => x.BatchName, StringComparer.CurrentCulture)
            .ThenBy(x => x.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    // ---------- Sales ----------

    /// <summary>
    /// A page of sales history. <paramref name="limit"/> counts ORDERS, not rows.
    /// <para><paramref name="from"/> and <paramref name="to"/> are instants, and the client
    /// works them out: "today" means today in Ho Chi Minh City, and only the browser knows
    /// that. Passing a bare date would leave the server guessing at a timezone.</para>
    /// </summary>
    public async Task<SalesPageDto> ListSalesAsync(
        int? limit, int? offset, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var take = limit is null or < 1 ? DefaultSalePage : Math.Min(limit.Value, MaxSaleHistory);
        var skip = Math.Max(offset ?? 0, 0);

        var (orders, amount) = await sales.SummariseAsync(from, to, ct);
        var rows = await sales.ListPageAsync(skip, take, from, to, ct);

        var items = rows
            .Select(r => new SaleRowDto(
                r.Sale.Id, r.Sale.ProductId, r.Sale.BatchId, r.Sale.Quantity, r.Sale.SellPrice,
                r.Sale.ShippingFee, r.Sale.Note, r.Sale.SaleGroupId, r.Sale.SoldAt,
                r.ProductName, r.BatchName, r.BatchPriority))
            .ToList();

        return new SalesPageDto(orders, amount, take, skip, items);
    }

    /// <returns><c>null</c> when no such batch belongs to the current user.</returns>
    public async Task<IReadOnlyList<SaleDto>?> ListSalesByBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        if (await batches.FindAsync(batchId, ct) is null) return null;

        var rows = await sales.ListByBatchAsync(batchId, ct);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// Records an order, which may draw on several batches at once.
    /// </summary>
    public async Task<IReadOnlyList<SaleDto>> RecordSaleAsync(
        RecordSaleRequest body, CancellationToken ct = default)
    {
        var items = body.Items ?? [];
        if (items.Count == 0) throw new ValidationException("Đơn hàng không có sản phẩm nào.");
        if (body.ShippingFee < 0) throw new ValidationException("Phí ship không được âm.");

        // One group id only when the order really spans several lines. A single sale keeps
        // a null group, exactly as before, so old rows and new ones look alike.
        var groupId = items.Count > 1 ? Guid.NewGuid() : (Guid?)null;
        var rows = new List<Sale>();

        // Two lines of the same product in one order both have to fit within what is left,
        // so the running total is what gets checked, not each line on its own.
        var taken = new Dictionary<Guid, long>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Quantity < 1) throw new ValidationException("Số lượng bán phải lớn hơn 0.");
            if (item.SellPrice < 0) throw new ValidationException("Giá bán không được âm.");

            var product = await products.FindAsync(item.ProductId, ct)
                ?? throw new ValidationException("Sản phẩm không tồn tại.");

            var available = await sales.AvailableAsync(product.Id, ct: ct);
            var already = taken.GetValueOrDefault(product.Id);
            if (already + item.Quantity > available)
                throw new ValidationException(
                    $"Không đủ tồn kho cho '{product.Name}': còn {available - already}, yêu cầu bán {item.Quantity}.");

            taken[product.Id] = already + item.Quantity;

            rows.Add(new Sale
            {
                UserId = user.Id,
                ProductId = product.Id,
                BatchId = product.BatchId,
                Quantity = item.Quantity,
                SellPrice = item.SellPrice,
                // Shipping and the note belong to the order, so they ride on the first line
                // only. Repeating them would count postage once per line.
                ShippingFee = i == 0 ? body.ShippingFee : 0m,
                Note = i == 0 ? Clean(body.Note) : null,
                SaleGroupId = groupId,
                SoldAt = body.SoldAt,
            });
        }

        sales.AddRange(rows);
        await uow.SaveChangesAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    /// <returns><c>null</c> when no such sale belongs to the current user.</returns>
    public async Task<SaleDto?> UpdateSaleAsync(Guid id, UpdateSaleRequest body, CancellationToken ct = default)
    {
        var row = await sales.FindAsync(id, ct);
        if (row is null) return null;

        if (body.Quantity < 1) throw new ValidationException("Số lượng bán phải lớn hơn 0.");
        if (body.SellPrice < 0) throw new ValidationException("Giá bán không được âm.");
        if (body.ShippingFee < 0) throw new ValidationException("Phí ship không được âm.");

        // This line is excluded from the sum, or editing a sale of 3 down to 2 would be
        // measured against stock that already counts those 3 as gone.
        var available = await sales.AvailableAsync(row.ProductId, ignoreSaleId: row.Id, ct: ct);
        if (body.Quantity > available)
            throw new ValidationException($"Không đủ tồn kho: còn {available}, yêu cầu bán {body.Quantity}.");

        row.Quantity = body.Quantity;
        row.SellPrice = body.SellPrice;
        row.ShippingFee = body.ShippingFee;
        row.SoldAt = body.SoldAt;
        row.Note = Clean(body.Note);

        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <returns><c>false</c> when no such sale belongs to the current user.</returns>
    public async Task<bool> DeleteSaleAsync(Guid id, CancellationToken ct = default)
    {
        var row = await sales.FindAsync(id, ct);
        if (row is null) return false;

        sales.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Deletes every line of one order.</summary>
    /// <returns>How many lines went. Zero means the caller has no such order.</returns>
    public async Task<int> DeleteSaleGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        var rows = await sales.ListByGroupAsync(groupId, ct);
        if (rows.Count == 0) return 0;

        sales.RemoveRange(rows);
        await uow.SaveChangesAsync(ct);
        return rows.Count;
    }

    // ---------- Damages ----------

    /// <returns><c>null</c> when no such batch belongs to the current user.</returns>
    public async Task<IReadOnlyList<DamageDto>?> ListDamagesAsync(Guid batchId, CancellationToken ct = default)
    {
        if (await batches.FindAsync(batchId, ct) is null) return null;

        var rows = await damages.ListByBatchAsync(batchId, ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<DamageDto> AddDamageAsync(AddDamageRequest body, CancellationToken ct = default)
    {
        if (body.Quantity < 1) throw new ValidationException("Số lượng hư phải lớn hơn 0.");

        var product = await products.FindAsync(body.ProductId, ct)
            ?? throw new ValidationException("Sản phẩm không tồn tại.");

        var available = await sales.AvailableAsync(product.Id, ct: ct);
        if (body.Quantity > available)
            throw new ValidationException($"Không đủ tồn để ghi hư: còn {available}, yêu cầu {body.Quantity}.");

        var row = new ProductDamage
        {
            UserId = user.Id,
            ProductId = product.Id,
            BatchId = product.BatchId,
            Quantity = body.Quantity,
            Note = Clean(body.Note),
        };
        damages.Add(row);
        await uow.SaveChangesAsync(ct);

        return ToDto(row);
    }

    /// <returns><c>false</c> when no such write-off belongs to the current user.</returns>
    public async Task<bool> DeleteDamageAsync(Guid id, CancellationToken ct = default)
    {
        var row = await damages.FindAsync(id, ct);
        if (row is null) return false;

        damages.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    // ---------- Mapping and small guards ----------

    private async Task<ProductStockDto?> FindStockAsync(Guid productId, Guid batchId, CancellationToken ct)
    {
        var rows = await products.StockByBatchAsync(batchId, ct);
        var row = rows.FirstOrDefault(x => x.Id == productId);
        return row is null ? null : ToDto(row);
    }

    private static BatchSummaryDto ToDto(BatchSummary s, int? priority) =>
        new(s.Id, s.Name, s.ImportDate.ToString(DayFormat), s.TotalCost, s.Note, priority, s.CreatedAt,
            s.ProductCount, s.TotalQty, s.SoldQty, s.DamagedQty, s.RemainingQty, s.Revenue, s.Profit);

    private static ProductStockDto ToDto(ProductStock p) =>
        new(p.Id, p.BatchId, p.Name, p.Quantity, p.SellPrice, p.CreatedAt,
            p.SoldQty, p.DamagedQty, p.Remaining, p.Revenue);

    private static SaleDto ToDto(Sale s) =>
        new(s.Id, s.ProductId, s.BatchId, s.Quantity, s.SellPrice, s.ShippingFee,
            s.Note, s.SaleGroupId, s.SoldAt);

    private static DamageDto ToDto(ProductDamage d) =>
        new(d.Id, d.ProductId, d.BatchId, d.Quantity, d.Note, d.CreatedAt);

    private static string Required(string? value, string message)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) throw new ValidationException(message);
        return v;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int Guard(int value, string message)
    {
        if (value < 0) throw new ValidationException(message);
        return value;
    }

    private static decimal GuardMoney(decimal value, string message)
    {
        if (value < 0) throw new ValidationException(message);
        return value;
    }

    private static DateOnly ParseDay(string? raw, string message) =>
        DateOnly.TryParseExact(raw, DayFormat, out var d) ? d : throw new ValidationException(message);
}
