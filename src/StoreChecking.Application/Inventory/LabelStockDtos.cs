namespace StoreChecking.Application.Inventory;

/// <summary>
/// Turns label text into the key a suggestion is stored and looked up under.
/// <para>Trim, collapse runs of whitespace, lower-case. The inventory really does contain
/// both <c>'Basic'</c> and <c>'Basic '</c>, so comparing raw text makes those two
/// different things.</para>
/// </summary>
public static class LabelText
{
    public static string Key(string? s) =>
        string.Join(' ', (s ?? "").ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>What to show a person: trimmed and collapsed, but left as it was typed.</summary>
    public static string Display(string? s) =>
        string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

// ---------- Suggestions ----------

/// <summary>
/// One label wording and the kind it was deducted as last time.
/// <para>Carries <c>KindId</c> rather than a product id: products come and go with each
/// batch, so a remembered product id would be pointing at an empty or deleted row within
/// weeks. The kind outlives the batches, and the client turns it back into whichever
/// batch currently has stock.</para>
/// </summary>
public record LabelAliasDto(
    Guid Id, string SourceName, string Variant, string Display, Guid KindId, int Units);

// ---------- Decisions ----------

/// <summary>
/// One decision about one label.
/// </summary>
/// <param name="Status">
/// <c>deducted</c> or <c>skipped</c>. There is no third value: "not decided" is the
/// absence of a row.
/// </param>
/// <param name="CanUndo">
/// Worked out from <paramref name="ActedAt"/> and the one-week window, on the server. The
/// client must not compute it from its own clock — a phone an hour fast would offer an
/// undo the server then refuses.
/// </param>
public record LabelActionDto(
    string OrderCode, string Status, Guid? SaleGroupId, int UnitsTotal,
    DateTimeOffset ActedAt, bool CanUndo);

/// <summary>
/// Everything the deduction screen needs, in one request.
/// </summary>
/// <param name="Codes">
/// EVERY code already decided, at any age — what the pending list subtracts. Separate
/// from <paramref name="Recent"/> on purpose: the undo list is windowed, this must not be,
/// or labels dealt with last month come back as work to do.
/// </param>
/// <param name="Recent">The last week's decisions, newest first.</param>
public record LabelActionsDto(IReadOnlyList<string> Codes, IReadOnlyList<LabelActionDto> Recent);

/// <summary>
/// One label line, and what the person decided it is.
/// </summary>
/// <param name="SourceName">Product name as printed. Kept only to remember the suggestion.</param>
/// <param name="Variant">Variation as printed. Same.</param>
/// <param name="LabelQty">
/// The count printed on the label. Used to work out the multiplier worth remembering —
/// one label item that turned out to be two units of stock is a combo, and next time the
/// same wording should pre-fill as two again.
/// </param>
/// <param name="ProductId">The product row the person picked, in a specific batch.</param>
/// <param name="Quantity">Units of stock to take off. What they confirmed, not a guess.</param>
public record DeductLine(
    string? SourceName, string? Variant, int LabelQty, Guid ProductId, int Quantity);

/// <summary>Take one label's stock off, from choices a person made.</summary>
public record DeductFromLabelRequest(string OrderCode, IReadOnlyList<DeductLine>? Lines);

/// <summary>
/// Mark labels as looked at and deliberately passed over.
/// <para>Takes a list rather than one code because clearing a backlog is the normal use:
/// tick the lot, pass them over in one go. One request per label would be hundreds of
/// round trips for a thing the person thinks of as a single action.</para>
/// </summary>
public record SkipLabelRequest(IReadOnlyList<string>? OrderCodes);

/// <summary>
/// What a skip did.
/// </summary>
/// <param name="AlreadyDecided">
/// Codes passed over because something had already been decided about them. Reported
/// rather than swallowed: a screen that was a minute out of date should say so, not
/// quietly do less than it was asked.
/// </param>
public record SkipLabelsResult(int Skipped, IReadOnlyList<string> AlreadyDecided);

/// <summary>What one product lost.</summary>
public record DeductedProduct(Guid ProductId, string ProductName, string BatchName, int Quantity);

/// <summary>What the deduction did, so the screen can say it rather than just go quiet.</summary>
public record DeductFromLabelResult(
    string OrderCode, Guid SaleGroupId, int UnitsTotal, IReadOnlyList<DeductedProduct> Products);
