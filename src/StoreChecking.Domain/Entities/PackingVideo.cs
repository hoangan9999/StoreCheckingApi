using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>
/// One packing recording, kept so an order can be looked up later if a customer disputes
/// what was in the parcel.
/// <para>Metadata only. The video file itself lives on the NAS and always did — Supabase
/// never held it — which is why this module moves across with nothing to migrate but rows.
/// <c>Filename</c> is the link between the two: <c>&lt;order_code&gt;_&lt;seq&gt;.&lt;ext&gt;</c>.</para>
/// </summary>
public class PackingVideo : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    /// <summary>Order code, scanned from a QR label or typed in.</summary>
    public string OrderCode { get; set; } = "";

    /// <summary>Which recording this is for that order — 1, 2, 3…</summary>
    public int Seq { get; set; }

    public string? Note { get; set; }

    /// <summary>Name of the actual file on the NAS. Null on rows written before the column existed.</summary>
    public string? Filename { get; set; }

    /// <summary>When it was filmed. Differs from CreatedAt for rows imported from the NAS.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
