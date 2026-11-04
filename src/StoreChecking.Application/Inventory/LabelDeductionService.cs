using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Common;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Inventory;

/// <summary>
/// Takes an order's stock off, from the product list read off its shipping label.
/// <para>Packing already scans the tracking code, and uploading the label already reads
/// the lines on it. What was missing was anything connecting that text to stock, because
/// a label carries no id — so the connection is made once per spelling, by hand, and
/// remembered as a <see cref="LabelItemAlias"/>.</para>
/// <para>⚠️ Nothing here guesses. A spelling that has not been mapped deducts nothing and
/// is reported. Matching two pieces of free text approximately and then taking stock off
/// would mean the wrong product quietly losing units, discovered at a stock-take with no
/// way to tell which order did it.</para>
/// </summary>
public sealed class LabelDeductionService(
    ICurrentUser user,
    IProductKindRepository kinds,
    ILabelItemAliasRepository aliases,
    ILabelDeductionRepository deductions,
    IProductRepository products,
    ISaleRepository sales,
    IBatchRepository batches,
    IUnitOfWork uow)
{
    // ---------- Kinds ----------

    /// <summary>Every kind with what is left of it, for the mapping screen's picker.</summary>
    public async Task<IReadOnlyList<ProductKindDto>> ListKindsAsync(CancellationToken ct = default)
    {
        var all = await kinds.ListAsync(ct);
        var stock = await products.InStockAsync(ct);

        return all.Select(k =>
        {
            var mine = stock.Where(s => s.KindId == k.Id).ToList();
            return new ProductKindDto(k.Id, k.Name, mine.Sum(s => s.Remaining), mine.Count);
        }).ToList();
    }

    // ---------- Aliases ----------

    public async Task<IReadOnlyList<LabelAliasDto>> ListAliasesAsync(CancellationToken ct = default)
    {
        var rows = await aliases.ListAsync(ct);
        var byId = (await kinds.ListAsync(ct)).ToDictionary(k => k.Id, k => k.Name);

        return rows.Select(a => new LabelAliasDto(
            a.Id, a.SourceName, a.Variant, a.Display,
            a.KindId,
            a.KindId is Guid id && byId.TryGetValue(id, out var n) ? n : null,
            a.Units, a.UpdatedAt)).ToList();
    }

    /// <summary>
    /// Map a spelling to a kind, or mark it ignored (<c>KindId</c> null).
    /// <para>Upsert, because the screen's job is as much correcting a wrong mapping as
    /// adding a new one, and those should not be two different buttons.</para>
    /// </summary>
    public async Task<LabelAliasDto> SaveAliasAsync(
        SaveLabelAliasRequest body, CancellationToken ct = default)
    {
        var nameKey = LabelText.Key(body.SourceName);
        var variantKey = LabelText.Key(body.Variant);
        if (nameKey.Length == 0 && variantKey.Length == 0)
            throw new ValidationException("Dòng trên nhãn không có tên lẫn phân loại, không ghép được.");

        // Ignoring a line has no sensible multiplier, so it is pinned to 1 rather than
        // left at whatever the caller happened to send.
        var units = body.KindId is null ? 1 : body.Units;
        if (body.KindId is not null && units < 1)
            throw new ValidationException("Số cái mỗi món phải lớn hơn 0.");

        if (body.KindId is Guid kindId && await kinds.FindAsync(kindId, ct) is null)
            throw new ValidationException("Mặt hàng không tồn tại.");

        var row = await aliases.FindByKeyAsync(nameKey, variantKey, ct);
        if (row is null)
        {
            row = new LabelItemAlias
            {
                UserId = user.Id,
                SourceName = nameKey,
                Variant = variantKey,
                Display = Label(body.SourceName, body.Variant),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            aliases.Add(row);
        }

        row.KindId = body.KindId;
        row.Units = units;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);

        var kindName = body.KindId is Guid k ? (await kinds.FindAsync(k, ct))?.Name : null;
        return new LabelAliasDto(
            row.Id, row.SourceName, row.Variant, row.Display, row.KindId, kindName, row.Units, row.UpdatedAt);
    }

    /// <returns><c>false</c> when no such alias belongs to the current user.</returns>
    public async Task<bool> DeleteAliasAsync(Guid id, CancellationToken ct = default)
    {
        var row = await aliases.FindAsync(id, ct);
        if (row is null) return false;
        aliases.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    // ---------- Deduction ----------

    /// <summary>Order codes already deducted, so a client knows which labels to skip.</summary>
    public Task<IReadOnlyList<string>> ListDeductedCodesAsync(CancellationToken ct = default) =>
        deductions.ListCodesAsync(ct);

    /// <summary>
    /// Take one order's stock off, exactly once.
    /// <para>All or nothing. If any kind on the label is short, nothing is deducted and
    /// nothing is marked: short stock means the figures were already wrong, and taking off
    /// what happens to be there while marking the order done would hide that and leave no
    /// way to retry after it is corrected.</para>
    /// </summary>
    public async Task<DeductFromLabelResult> DeductAsync(
        DeductFromLabelRequest body, CancellationToken ct = default)
    {
        var code = (body.OrderCode ?? "").Trim();
        if (code.Length == 0) throw new ValidationException("Thiếu mã đơn.");

        var lines = body.Items ?? [];
        if (lines.Count == 0) throw new ValidationException("Nhãn không có dòng sản phẩm nào.");

        // Asked first, and answered as success rather than an error: re-uploading a label
        // is a normal thing to do, and the caller should be able to tell "already done"
        // from "could not".
        if (await deductions.FindAsync(code, ct) is { } done)
            return new DeductFromLabelResult(
                code, false, true, done.SaleGroupId, done.UnitsTotal, [], [], []);

        var wanted = new Dictionary<Guid, int>();
        var unmapped = new List<LabelLineIssue>();
        var ignored = new List<LabelLineIssue>();

        foreach (var line in lines)
        {
            if (line.Qty < 1) throw new ValidationException("Số lượng trên nhãn phải lớn hơn 0.");

            var alias = await aliases.FindByKeyAsync(
                LabelText.Key(line.Name), LabelText.Key(line.Variant), ct);

            if (alias is null)
            {
                unmapped.Add(new LabelLineIssue(Label(line.Name, line.Variant), "chưa ghép"));
                continue;
            }

            if (alias.KindId is not Guid kindId)
            {
                ignored.Add(new LabelLineIssue(alias.Display, "đã đánh dấu bỏ qua"));
                continue;
            }

            wanted[kindId] = wanted.GetValueOrDefault(kindId) + line.Qty * alias.Units;
        }

        // Nothing to take off, so no sale and — importantly — no marker. Marking an order
        // done here would block the retry that mapping the spelling is supposed to enable.
        if (wanted.Count == 0)
            return new DeductFromLabelResult(code, false, false, null, 0, [], unmapped, ignored);

        var byId = (await kinds.ListAsync(ct)).ToDictionary(k => k.Id);
        var order = await BatchOrderAsync(ct);
        var rows = new List<Sale>();
        var taken = new List<DeductedKind>();
        var short_ = new List<string>();

        foreach (var (kindId, need) in wanted)
        {
            var kindName = byId.TryGetValue(kindId, out var k) ? k.Name : "?";
            var left = need;

            foreach (var p in (await products.ListByKindAsync(kindId, ct))
                     .OrderBy(p => order.GetValueOrDefault(p.BatchId, (int.MaxValue, DateOnly.MaxValue)))
                     .ThenBy(p => p.CreatedAt))
            {
                if (left == 0) break;
                var available = await sales.AvailableAsync(p.Id, ct: ct);
                if (available <= 0) continue;

                var take = (int)Math.Min(left, available);
                rows.Add(new Sale
                {
                    UserId = user.Id,
                    ProductId = p.Id,
                    BatchId = p.BatchId,
                    Quantity = take,
                    // The price set on the batch. A label never prints one.
                    SellPrice = p.SellPrice,
                    ShippingFee = 0m,
                    Note = null,
                    SoldAt = DateTimeOffset.UtcNow,
                });
                left -= take;
            }

            if (left > 0) short_.Add($"'{kindName}': cần {need}, còn {need - left}");
            else taken.Add(new DeductedKind(kindId, kindName, need));
        }

        if (short_.Count > 0)
            throw new ValidationException("Không đủ tồn kho — " + string.Join("; ", short_) + ".");

        // One group for the whole order, even when it is a single line, so the order's
        // lines can be found again and undone together.
        var groupId = Guid.NewGuid();
        var total = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].SaleGroupId = groupId;
            // Shipping and the note belong to the order, so they ride on the first line
            // only, exactly as the manual path does it.
            if (i == 0) rows[i].Note = "Tự trừ từ nhãn " + code;
            total += rows[i].Quantity;
        }

        sales.AddRange(rows);
        deductions.Add(new LabelDeduction
        {
            UserId = user.Id,
            OrderCode = code,
            SaleGroupId = groupId,
            UnitsTotal = total,
            DeductedAt = DateTimeOffset.UtcNow,
        });

        // One save, so the sale lines and the "already done" marker land together. Written
        // separately, a failure between them either loses the stock or blocks it forever.
        await uow.SaveChangesAsync(ct);

        return new DeductFromLabelResult(code, true, false, groupId, total, taken, unmapped, ignored);
    }

    /// <summary>
    /// Which batch to take from first: the priority the user set, then the oldest.
    /// <para>Batches with no priority go last rather than first — an unset priority means
    /// "not decided", and guessing it is the most urgent would quietly override the order
    /// the user did set.</para>
    /// </summary>
    private async Task<Dictionary<Guid, (int, DateOnly)>> BatchOrderAsync(CancellationToken ct) =>
        (await batches.ListAsync(ct)).ToDictionary(
            b => b.Id, b => (b.Priority ?? int.MaxValue, b.ImportDate));

    private static string Label(string? name, string? variant)
    {
        var n = LabelText.Display(name);
        var v = LabelText.Display(variant);
        if (n.Length == 0) return v;
        return v.Length == 0 ? n : n + " — " + v;
    }
}
