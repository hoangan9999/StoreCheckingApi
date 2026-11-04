namespace StoreChecking.Application.Inventory;

/// <summary>
/// Turns label text into the key an alias is stored and looked up under.
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

// ---------- Kinds ----------

/// <summary>A kind, with how much stock stands behind it right now.</summary>
public record ProductKindDto(Guid Id, string Name, long Remaining, int BatchCount);

// ---------- Aliases ----------

/// <summary>
/// One label spelling and what it means.
/// <para>A null <c>KindId</c> means the line is ignored on purpose.</para>
/// </summary>
public record LabelAliasDto(
    Guid Id, string SourceName, string Variant, string Display,
    Guid? KindId, string? KindName, int Units, DateTimeOffset UpdatedAt);

/// <summary>
/// Map a label spelling, or mark it ignored.
/// <para>Upsert on (<c>SourceName</c>, <c>Variant</c>) so saving the same line twice
/// corrects it instead of failing.</para>
/// </summary>
public record SaveLabelAliasRequest(
    string SourceName, string Variant, Guid? KindId, int Units);

// ---------- Deduction ----------

/// <summary>One line read off a label.</summary>
public record LabelLine(string Name, string Variant, int Qty);

/// <summary>Take an order's stock off, once.</summary>
public record DeductFromLabelRequest(string OrderCode, IReadOnlyList<LabelLine>? Items);

/// <summary>A line that could not be used, and why.</summary>
public record LabelLineIssue(string Display, string Reason);

/// <summary>What one kind lost.</summary>
public record DeductedKind(Guid KindId, string KindName, int Units);

/// <summary>
/// The outcome, which is deliberately not just a status code.
/// <para>A caller has to be able to tell three different things apart: stock came off;
/// nothing came off because nothing on the label is tracked; and nothing came off because
/// a spelling is new and wants mapping. Only the last one is something to act on.</para>
/// </summary>
/// <param name="AlreadyDone">This order had been done before — not an error.</param>
/// <param name="Unmapped">Spellings never seen. These are what the mapping screen shows.</param>
/// <param name="Ignored">Spellings mapped to "ignore", listed so the report adds up.</param>
public record DeductFromLabelResult(
    string OrderCode,
    bool Deducted,
    bool AlreadyDone,
    Guid? SaleGroupId,
    int UnitsTotal,
    IReadOnlyList<DeductedKind> Kinds,
    IReadOnlyList<LabelLineIssue> Unmapped,
    IReadOnlyList<LabelLineIssue> Ignored);
