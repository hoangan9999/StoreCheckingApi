using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>
/// A thing that gets sold, independent of which batch it came in on.
/// <para>Before this existed, "Basic" was not represented anywhere: there were only
/// <see cref="Product"/> rows, one per batch, that happened to share a name. Anything
/// wanting to point at "Basic" therefore had to compare the name as text, and comparing
/// text means one rename breaks the reference silently — the code still runs, nothing
/// errors, it just stops matching.</para>
/// <para><see cref="Id"/> is the identity. <see cref="Name"/> is only what gets shown and
/// may be changed freely.</para>
/// </summary>
public class ProductKind : IOwnedByUser
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Display text. NOT the identity — never look a kind up by this.</summary>
    public string Name { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One spelling a carrier's label uses for a kind, and how many units one of them is.
/// <para>A kind has many of these: they are aliases, not a one-to-one mapping. The text
/// comes from TikTok/SPX and is the one thing here that still has to be matched as a
/// string, because a shipping label carries no id that points back at stock.</para>
/// <para>Being aliases is what keeps that bearable. Renaming a variation at the carrier
/// produces text that matches nothing, so the line lands in the unmapped list where it
/// can be seen; adding another alias fixes it and the old one stays harmless. Renaming a
/// kind changes nothing at all, because this holds <see cref="KindId"/> and not a name.
/// </para>
/// </summary>
public class LabelItemAlias : IOwnedByUser
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Label's product name, normalised by <c>LabelText.Key</c>.</summary>
    public string SourceName { get; set; } = "";

    /// <summary>Label's variation, normalised by <c>LabelText.Key</c>.</summary>
    public string Variant { get; set; } = "";

    /// <summary>
    /// As first seen, for showing to a person. The key is lower-cased, so displaying it
    /// would leave the user unable to recognise their own product.
    /// </summary>
    public string Display { get; set; } = "";

    /// <summary><c>null</c> means ignore this line: a product whose stock is not tracked.</summary>
    public Guid? KindId { get; set; }

    /// <summary>
    /// Units of stock one label item equals. "Protect Basic (Combo 2 cái )" is 2.
    /// <para>Without it a deduction is wrong by a factor of two or ten while still looking
    /// entirely plausible.</para>
    /// </summary>
    public int Units { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}


/// <summary>
/// What was decided about one label: its stock was taken off, or it was deliberately
/// passed over. A label with no row here is one nobody has looked at yet.
/// <para>Nothing decides this on its own. A label prints a name and a quantity, and a
/// batch holds products with a name and a quantity, with no id in common — so there is no
/// ground on which code could claim that a given line means a given product. The person
/// packing the order knows; this records what they said.</para>
/// <para>It has to be a real row. The label's own JSON on the NAS is rewritten whole every
/// time the label is uploaded again, which would wipe the decision; and the sales table
/// cannot answer either, because the history reading it is filtered by date and capped, so
/// "not in the list" never means "not deducted".</para>
/// <para>The key is (<see cref="UserId"/>, <see cref="OrderCode"/>), which makes a second
/// deduction a database error rather than a rule somebody has to remember.</para>
/// </summary>
public class LabelStockAction : IOwnedByUser
{
    /// <summary>Stock came off for this label.</summary>
    public const string Deducted = "deducted";

    /// <summary>Looked at and passed over on purpose — not stock this shop tracks.</summary>
    public const string Skipped = "skipped";

    public Guid UserId { get; set; }

    /// <summary>Tracking code — what packing scanned, and the label's file name on the NAS.</summary>
    public string OrderCode { get; set; } = "";

    /// <summary><see cref="Deducted"/> or <see cref="Skipped"/>.</summary>
    public string Status { get; set; } = Deducted;

    /// <summary>
    /// The sale lines this created; <c>null</c> for a skip.
    /// <para>Always set when stock came off, even for a single line, because it is the
    /// only way back from "this order" to "these sale rows" — which is what undoing needs.
    /// (The manual selling path leaves a one-line order ungrouped so old and new rows look
    /// alike; that reasoning does not apply here.)</para>
    /// </summary>
    public Guid? SaleGroupId { get; set; }

    /// <summary>Units taken off, so a check never has to re-add the sale lines. 0 for a skip.</summary>
    public int UnitsTotal { get; set; }

    /// <summary>When the decision was made. The undo window is measured from here.</summary>
    public DateTimeOffset ActedAt { get; set; }
}
