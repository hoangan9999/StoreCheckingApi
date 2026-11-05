using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Common;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Inventory;

/// <summary>
/// Taking an order's stock off from its shipping label — with a person deciding, every time.
/// <para>⚠️ The earlier version of this tried to work out by itself which product a label
/// line meant. It cannot be done, and the reason is in the data: a label prints a name and
/// a count, a batch holds products with a name and a count, and the two share no id. There
/// is nothing to join on. Matching the text approximately and then moving stock means the
/// wrong product quietly losing units, found at a stock-take with no way to tell which
/// order did it.</para>
/// <para>So nothing here decides anything. Uploading a label files it against the packing
/// video and stops. Deduction is a separate, deliberate act: the person opens the list of
/// labels nobody has dealt with, reads what is printed, picks the product out of a
/// dropdown and confirms. A label that is not stock this shop tracks gets passed over just
/// as deliberately.</para>
/// <para>What IS remembered is the choice, as a <see cref="LabelItemAlias"/>, and only to
/// pre-select the dropdown next time the same wording turns up. Twenty identical orders
/// should not be twenty identical decisions — but they are still twenty confirmations.</para>
/// </summary>
public sealed class LabelStockService(
    ICurrentUser user,
    IProductKindRepository kinds,
    ILabelItemAliasRepository aliases,
    ILabelStockActionRepository actions,
    IProductRepository products,
    ISaleRepository sales,
    IBatchRepository batches,
    IUnitOfWork uow)
{
    /// <summary>
    /// How long a decision stays undoable.
    /// <para>The history is shown for the same week. Past it the decision stands: undoing
    /// a two-month-old deduction would silently rewrite revenue in a month already read
    /// and reported on.</para>
    /// </summary>
    public static readonly TimeSpan UndoWindow = TimeSpan.FromDays(7);

    // ---------- Suggestions ----------

    /// <summary>
    /// Wordings already deducted once, so the client can pre-select the dropdown.
    /// <para>Only ones pointing at a kind. A row with no kind is left over from the
    /// version that could mark a wording "ignore"; it suggests nothing now, and skipping
    /// is a decision about a whole label rather than one line of it.</para>
    /// </summary>
    public async Task<IReadOnlyList<LabelAliasDto>> ListAliasesAsync(CancellationToken ct = default) =>
        (await aliases.ListAsync(ct))
        .Where(a => a.KindId is not null)
        .Select(a => new LabelAliasDto(a.Id, a.SourceName, a.Variant, a.Display, a.KindId!.Value, a.Units))
        .ToList();

    // ---------- Decisions ----------

    /// <summary>Which labels are done, and the week's worth that can still be undone.</summary>
    public async Task<LabelActionsDto> ListActionsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var codes = await actions.ListCodesAsync(ct);
        var recent = await actions.ListSinceAsync(now - UndoWindow, ct);

        return new LabelActionsDto(
            codes,
            recent.Select(r => new LabelActionDto(
                r.OrderCode, r.Status, r.SaleGroupId, r.UnitsTotal, r.ActedAt,
                now - r.ActedAt < UndoWindow)).ToList());
    }

    /// <summary>
    /// Take one label's stock off, from the kinds a person picked.
    /// <para>All or nothing. If any product is short, nothing moves and nothing is marked:
    /// short stock means the figures were already wrong, and taking off what happens to be
    /// there while recording the label as done would hide that and leave no way to retry
    /// once it is corrected.</para>
    /// </summary>
    public async Task<DeductFromLabelResult> DeductAsync(
        DeductFromLabelRequest body, CancellationToken ct = default)
    {
        var code = (body.OrderCode ?? "").Trim();
        if (code.Length == 0) throw new ValidationException("Thiếu mã đơn.");

        var lines = body.Lines ?? [];
        if (lines.Count == 0) throw new ValidationException("Chưa chọn sản phẩm nào để trừ.");

        await RefuseIfDecided(code, ct);

        // Resolve every kind first, so a bad id fails before anything is written.
        var kindNames = (await kinds.ListAsync(ct)).ToDictionary(k => k.Id, k => k.Name);
        foreach (var line in lines)
        {
            if (line.Quantity < 1) throw new ValidationException("Số cái phải lớn hơn 0.");
            if (!kindNames.ContainsKey(line.KindId))
                throw new ValidationException("Mặt hàng không tồn tại hoặc không thuộc về bạn.");
        }

        // Two lines of a label can land on the same kind — a combo listed twice, or two
        // variations the person maps to one thing. Summed before drawing stock, or each
        // half passes on its own and together they oversell.
        var wanted = lines
            .GroupBy(l => l.KindId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var order = (await batches.ListAsync(ct))
            .ToDictionary(b => b.Id, b => (Rank: b.Priority ?? int.MaxValue, b.ImportDate, b.Name));

        // One group for the whole label, even when it is a single line, so the order's
        // rows can be found again and undone together.
        var groupId = Guid.NewGuid();
        var rows = new List<Sale>();
        var taken = new List<DeductedProduct>();
        var short_ = new List<string>();
        var total = 0;

        foreach (var (kindId, need) in wanted)
        {
            var left = need;

            // Which batch to draw from: the priority the user set, then the oldest stock.
            // Batches with no priority go LAST — unset means "not decided", and treating
            // that as most urgent would quietly override the order they did set.
            foreach (var p in (await products.ListByKindAsync(kindId, ct))
                     .OrderBy(p => order.GetValueOrDefault(p.BatchId).Rank)
                     .ThenBy(p => order.GetValueOrDefault(p.BatchId).ImportDate)
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
                    SoldAt = DateTimeOffset.UtcNow,
                    SaleGroupId = groupId,
                });

                taken.Add(new DeductedProduct(
                    p.Id, p.Name, order.GetValueOrDefault(p.BatchId).Name ?? "?", take));
                total += take;
                left -= take;
            }

            if (left > 0) short_.Add($"'{kindNames[kindId]}': cần {need}, còn {need - left}");
        }

        // Nothing has been handed to EF yet, so throwing here really does leave the books
        // untouched. Short stock means the figures were already wrong; taking off what
        // happens to be there while recording the label as done would hide that and leave
        // no way to retry once it is corrected.
        if (short_.Count > 0)
            throw new ValidationException("Không đủ tồn kho — " + string.Join("; ", short_) + ".");

        // Shipping and the note belong to the order, so the note rides on the first row
        // only, exactly as the manual selling path does it.
        if (rows.Count > 0) rows[0].Note = "Trừ tồn từ nhãn " + code;

        sales.AddRange(rows);
        await RememberAsync(lines, ct);
        actions.Add(new LabelStockAction
        {
            UserId = user.Id,
            OrderCode = code,
            Status = LabelStockAction.Deducted,
            SaleGroupId = groupId,
            UnitsTotal = total,
            ActedAt = DateTimeOffset.UtcNow,
        });

        // One save, so the sale rows and the record of the decision land together. Written
        // separately, a failure between them either loses the stock or blocks it forever.
        await uow.SaveChangesAsync(ct);

        return new DeductFromLabelResult(code, groupId, total, taken);
    }

    /// <summary>
    /// Record that labels were looked at and deliberately passed over.
    /// <para>Codes already decided are reported back rather than treated as failures: with
    /// a whole backlog ticked at once, one stale entry must not throw away the other two
    /// hundred, and it must not pass unmentioned either.</para>
    /// </summary>
    public async Task<SkipLabelsResult> SkipAsync(SkipLabelRequest body, CancellationToken ct = default)
    {
        var codes = (body.OrderCodes ?? [])
            .Select(c => (c ?? "").Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (codes.Count == 0) throw new ValidationException("Thiếu mã đơn.");

        // Read the decided set ONCE. Asking per code turns a two-hundred-label skip into
        // two hundred queries inside one request.
        var decided = (await actions.ListCodesAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var already = new List<string>();
        var added = 0;

        foreach (var code in codes)
        {
            if (!decided.Add(code)) { already.Add(code); continue; }

            actions.Add(new LabelStockAction
            {
                UserId = user.Id,
                OrderCode = code,
                Status = LabelStockAction.Skipped,
                SaleGroupId = null,
                UnitsTotal = 0,
                ActedAt = now,
            });
            added++;
        }

        if (added > 0) await uow.SaveChangesAsync(ct);
        return new SkipLabelsResult(added, already);
    }

    /// <summary>
    /// Undo a decision: the label goes back to the pending list, and a deduction's sale
    /// rows go with it.
    /// </summary>
    /// <returns><c>false</c> when the current user has no decision on that code.</returns>
    public async Task<bool> UndoAsync(string orderCode, CancellationToken ct = default)
    {
        var code = (orderCode ?? "").Trim();
        var row = await actions.FindAsync(code, ct);
        if (row is null) return false;

        if (DateTimeOffset.UtcNow - row.ActedAt >= UndoWindow)
            throw new ValidationException(
                "Quá 7 ngày rồi, không hoàn lại được nữa. Sửa bằng tay ở lịch sử bán của lô.");

        if (row.SaleGroupId is Guid groupId)
            sales.RemoveRange(await sales.ListByGroupAsync(groupId, ct));

        actions.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    private async Task RefuseIfDecided(string code, CancellationToken ct)
    {
        if (await actions.FindAsync(code, ct) is not { } done) return;

        throw new ValidationException(done.Status == LabelStockAction.Deducted
            ? $"Nhãn {code} đã trừ tồn rồi. Muốn làm lại thì hoàn lại ở mục Đã xử lý."
            : $"Nhãn {code} đã đánh dấu bỏ qua. Muốn trừ tồn thì hoàn lại ở mục Đã xử lý.");
    }

    /// <summary>
    /// Remember what each wording turned out to mean, for pre-selecting next time.
    /// <para>Stored against the product's KIND, not the product: batches run out and get
    /// deleted, so a remembered product id would be pointing at nothing within weeks.</para>
    /// <para>A wrong suggestion never needs repairing by hand — deducting the same wording
    /// as something else overwrites it here.</para>
    /// </summary>
    private async Task RememberAsync(
        IReadOnlyList<DeductLine> lines, CancellationToken ct)
    {
        // Grouped by wording, because one printed line can be split across several
        // products — a line of five that was really three Basic and two Premium. There is
        // no single answer to suggest for a wording like that, so the honest thing is to
        // suggest nothing and let the person split it again. Remembering whichever half
        // happened to be written last would pre-fill a figure that was never right.
        foreach (var group in lines.GroupBy(l => (LabelText.Key(l.SourceName), LabelText.Key(l.Variant))))
        {
            var (nameKey, variantKey) = group.Key;
            if (nameKey.Length == 0 && variantKey.Length == 0) continue;
            if (group.Count() > 1) continue;

            var line = group.First();
            var kindId = line.KindId;

            // "Protect Basic (Combo 2 cái)" prints a quantity of 1 and costs 2 units, so
            // the multiplier is what makes the next one pre-fill correctly. Anything that
            // does not divide evenly is not a combo — fall back to 1 rather than invent a
            // fraction.
            var units = line.LabelQty >= 1 && line.Quantity % line.LabelQty == 0
                ? line.Quantity / line.LabelQty
                : 1;

            var row = await aliases.FindByKeyAsync(nameKey, variantKey, ct);
            if (row is null)
            {
                row = new LabelItemAlias
                {
                    UserId = user.Id,
                    SourceName = nameKey,
                    Variant = variantKey,
                    Display = Label(line.SourceName, line.Variant),
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                aliases.Add(row);
            }

            row.KindId = kindId;
            row.Units = units;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static string Label(string? name, string? variant)
    {
        var n = LabelText.Display(name);
        var v = LabelText.Display(variant);
        if (n.Length == 0) return v;
        return v.Length == 0 ? n : n + " — " + v;
    }
}
