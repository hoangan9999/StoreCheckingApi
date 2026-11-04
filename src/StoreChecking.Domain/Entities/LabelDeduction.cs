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
/// Proof that one order's stock has already come off, so it cannot come off twice.
/// <para>This has to be a real row. The label's own JSON file on the NAS is rewritten
/// whole whenever the label is uploaded again, which would clear the flag; and the sales
/// table cannot answer the question either, because the history that reads it is filtered
/// by date and capped, so "not in the list" does not mean "not deducted".</para>
/// <para>The key is (<see cref="UserId"/>, <see cref="OrderCode"/>), which makes a second
/// deduction a database error rather than a rule somebody has to remember to check.</para>
/// </summary>
public class LabelDeduction : IOwnedByUser
{
    public Guid UserId { get; set; }

    /// <summary>Tracking code — what packing scanned, and the label's file name on the NAS.</summary>
    public string OrderCode { get; set; } = "";

    /// <summary>
    /// The sale lines this deduction created, or <c>null</c> when it created none.
    /// <para>Always set when this app did the deducting, even for a single line, so that
    /// "which sale lines came from this order" has an answer and they can be undone. (The
    /// manual selling path leaves a one-line order ungrouped, to keep old and new rows
    /// looking alike; that reasoning does not apply here.)</para>
    /// <para><c>null</c> means the order was settled OUTSIDE the app — stock entered by
    /// hand before this feature existed, see <c>db/020</c>. A made-up group id would be an
    /// id pointing at nothing, so the absence is recorded instead.</para>
    /// </summary>
    public Guid? SaleGroupId { get; set; }

    /// <summary>
    /// Units taken off, so a check does not have to re-add the sale lines.
    /// <para>0 goes with a null <see cref="SaleGroupId"/>: the true figure for an order
    /// this app deducted nothing for.</para>
    /// </summary>
    public int UnitsTotal { get; set; }

    public DateTimeOffset DeductedAt { get; set; }
}
