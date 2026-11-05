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
    IProductKindRepository kinds,
    ILabelStockActionRepository labelActions,
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

    /// <summary>
    /// The kind a product of this name belongs to, creating it the first time.
    /// <para>Every path that writes a product name goes through here, so a product always
    /// has a kind and anything mapping labels to stock has an id to point at. Typing an
    /// existing name again lands on the existing kind: that lookup by text is the one
    /// place it is allowed, and it exists precisely so a second "Basic" is never created.
    /// </para>
    /// <para><paramref name="pending"/> carries kinds made earlier in the same call but
    /// not yet saved, which the repository cannot see yet. Without it, a new batch listing
    /// "Basic" twice would try to insert that kind twice and hit the unique index.</para>
    /// </summary>
    private async Task<ProductKind> KindForAsync(
        string name, Dictionary<string, ProductKind> pending, CancellationToken ct)
    {
        var key = LabelText.Key(name);
        if (pending.TryGetValue(key, out var made)) return made;

        var found = await kinds.FindByKeyAsync(key, ct);
        if (found is not null)
        {
            pending[key] = found;
            return found;
        }

        // Id assigned here rather than left to the column default: the product rows being
        // built in this same call need it NOW to set their kind_id, and a database default
        // only arrives after the save.
        var row = new ProductKind
        {
            Id = Guid.NewGuid(), UserId = user.Id, Name = name, CreatedAt = DateTimeOffset.UtcNow,
        };
        kinds.Add(row);
        pending[key] = row;
        return row;
    }

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

        // A loop rather than Select: resolving the kind is a database lookup, and two
        // products in one batch may name the same kind.
        var pending = new Dictionary<string, ProductKind>();
        var rows = new List<Product>();
        foreach (var p in body.Products ?? [])
        {
            var kind = await KindForAsync(Required(p.Name, "Sản phẩm thiếu tên."), pending, ct);
            rows.Add(new Product
            {
                UserId = user.Id,
                BatchId = batch.Id,
                KindId = kind.Id,
                // Mirrors the kind, so a name typed with stray spaces lands tidy.
                Name = kind.Name,
                Quantity = Guard(p.Quantity ?? 0, "Số lượng không được âm."),
                SellPrice = GuardMoney(p.SellPrice ?? 0, "Giá bán không được âm."),
            });
        }

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

        var kind = await KindForAsync(Required(body.Name, "Thiếu tên sản phẩm."), [], ct);
        var row = new Product
        {
            UserId = user.Id,
            BatchId = batch.Id,
            KindId = kind.Id,
            Name = kind.Name,
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

        // Renaming re-resolves the kind, which moves this row to another kind when the new
        // name is one that already exists. That is the intent: the row now holds Premium,
        // not Basic. Aliases keep pointing at the kind they were mapped to.
        var kind = await KindForAsync(Required(body.Name, "Thiếu tên sản phẩm."), [], ct);
        row.KindId = kind.Id;
        row.Name = kind.Name;
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
                    p.SellPrice, p.Remaining, p.KindId);
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

        // The label's record of "deducted N" has to follow the rows it describes. Read
        // the siblings BEFORE touching this one, so the sum is over the real new figures
        // rather than over a change tracker halfway through being edited.
        if (row.SaleGroupId is Guid g)
        {
            var siblings = await sales.ListByGroupAsync(g, ct);
            await SyncLabelTotalAsync(g, siblings.Sum(x => x.Id == row.Id ? body.Quantity : x.Quantity), ct);
        }

        row.Quantity = body.Quantity;
        row.SellPrice = body.SellPrice;
        row.ShippingFee = body.ShippingFee;
        row.SoldAt = body.SoldAt;
        row.Note = Clean(body.Note);

        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <summary>
    /// Edit a whole order: every line, plus the shipping, date and note that belong to
    /// the order rather than to a line.
    /// <para>Exists next to <see cref="UpdateSaleAsync"/> because an order written by a
    /// label deduction is several rows at once, and fixing it one row at a time is
    /// several round trips that can half-succeed. Here it is one transaction: the order
    /// ends up as described, or it does not change at all.</para>
    /// </summary>
    /// <returns><c>null</c> when the current user has no order with that group id.</returns>
    public async Task<IReadOnlyList<SaleDto>?> UpdateSaleGroupAsync(
        Guid groupId, UpdateSaleGroupRequest body, CancellationToken ct = default)
    {
        var rows = await sales.ListByGroupAsync(groupId, ct);
        if (rows.Count == 0) return null;

        var want = body.Lines ?? [];
        if (want.Count == 0)
            throw new ValidationException("Đơn phải còn ít nhất một dòng. Muốn bỏ hẳn thì xoá đơn.");
        if (body.ShippingFee < 0) throw new ValidationException("Phí ship không được âm.");

        var byId = rows.ToDictionary(r => r.Id);
        if (want.Select(l => l.Id).Distinct().Count() != want.Count)
            throw new ValidationException("Một dòng bán được gửi lên hai lần.");

        foreach (var l in want)
        {
            if (!byId.ContainsKey(l.Id)) throw new ValidationException("Dòng bán không thuộc đơn này.");
            if (l.Quantity < 1) throw new ValidationException("Số lượng bán phải lớn hơn 0.");
            if (l.SellPrice < 0) throw new ValidationException("Giá bán không được âm.");
        }

        // Stock, per product, counting this order's own rows as free again.
        //
        // Not one check per line: two lines of the same order can draw on the same
        // product, and checking them separately lets each pass on its own while together
        // they oversell. And the capacity has to add back what this order already holds,
        // or raising a line from 3 to 4 is measured against stock that still counts the
        // 3 as gone.
        var held = rows.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => (long)g.Sum(r => r.Quantity));
        var need = want.GroupBy(l => byId[l.Id].ProductId).ToDictionary(g => g.Key, g => (long)g.Sum(l => l.Quantity));

        var short_ = new List<string>();
        foreach (var (productId, wantQty) in need)
        {
            var capacity = await sales.AvailableAsync(productId, ct: ct) + held.GetValueOrDefault(productId);
            if (wantQty > capacity)
            {
                var name = (await products.FindAsync(productId, ct))?.Name ?? "?";
                short_.Add($"'{name}': cần {wantQty}, còn {Math.Max(capacity, 0)}");
            }
        }

        if (short_.Count > 0)
            throw new ValidationException("Không đủ tồn kho — " + string.Join("; ", short_) + ".");

        var keep = want.ToDictionary(l => l.Id);
        var dropped = rows.Where(r => !keep.ContainsKey(r.Id)).ToList();
        if (dropped.Count > 0) sales.RemoveRange(dropped);

        // Shipping and the note ride on the FIRST remaining line only, exactly as the
        // selling and deduction paths write them. Left on every line, a two-line order
        // would read as having paid postage twice.
        var first = true;
        foreach (var l in want)
        {
            var row = byId[l.Id];
            row.Quantity = l.Quantity;
            row.SellPrice = l.SellPrice;
            row.SoldAt = body.SoldAt;
            row.ShippingFee = first ? body.ShippingFee : 0m;
            row.Note = first ? Clean(body.Note) : null;
            first = false;
        }

        await SyncLabelTotalAsync(groupId, want.Sum(l => l.Quantity), ct);
        await uow.SaveChangesAsync(ct);

        return want.Select(l => ToDto(byId[l.Id])).ToList();
    }

    /// <summary>
    /// Keep a label's "deducted N units" in step with the rows it describes.
    /// <para>Silent drift otherwise: the record says 5 while its lines add up to 3, and
    /// nothing in the app ever compares the two.</para>
    /// </summary>
    private async Task SyncLabelTotalAsync(Guid groupId, int units, CancellationToken ct)
    {
        foreach (var a in await labelActions.ListByGroupAsync(groupId, ct)) a.UnitsTotal = units;
    }

    /// <returns><c>false</c> when no such sale belongs to the current user.</returns>
    public async Task<bool> DeleteSaleAsync(Guid id, CancellationToken ct = default)
    {
        var row = await sales.FindAsync(id, ct);
        if (row is null) return false;

        // A row written by a label deduction is one line of one decision, and there is no
        // half of that decision. So deleting any of them undoes the whole label — which
        // also returns it to the pending list, where it can be done again properly. The
        // alternative is a record saying "deducted 5" sitting over rows adding up to 3,
        // wrong in a way nothing would ever point out.
        if (row.SaleGroupId is Guid groupId && await ClearLabelActionAsync(groupId, ct))
            sales.RemoveRange(await sales.ListByGroupAsync(groupId, ct));
        else
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

        await ClearLabelActionAsync(groupId, ct);
        sales.RemoveRange(rows);
        await uow.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// Drops the "this label is done" record for a group of sale rows about to go.
    /// <para>Without this the label stays marked for ever: its sale rows are gone, the
    /// stock is back, and the deduction screen still refuses to show it. Nothing would
    /// report that — it is exactly the kind of silence this project keeps paying for.</para>
    /// </summary>
    /// <returns><c>false</c> when the group did not come from a label.</returns>
    private async Task<bool> ClearLabelActionAsync(Guid groupId, CancellationToken ct)
    {
        var rows = await labelActions.ListByGroupAsync(groupId, ct);
        if (rows.Count == 0) return false;

        labelActions.RemoveRange(rows);
        return true;
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
