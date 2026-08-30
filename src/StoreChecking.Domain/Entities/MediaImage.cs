using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>
/// One picture in the album the daily video job draws from.
///
/// <para>Metadata only — the file itself sits on disk in the `media` volume, linked by
/// <c>Filename</c>, the same arrangement <see cref="PackingVideo"/> uses. Binary data in
/// Postgres would blow the nightly backup up from a few hundred KB to gigabytes, and that
/// backup is pushed over the network to the NAS.</para>
/// </summary>
public class MediaImage : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    /// <summary>Name of the actual file on disk. Generated, never the name the user gave.</summary>
    public string Filename { get; set; } = "";

    /// <summary>What it was called when uploaded. Shown in the album, never used as a path.</summary>
    public string OriginalName { get; set; } = "";

    public string ContentType { get; set; } = "image/jpeg";
    public long Bytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>
    /// How many videos have used this picture.
    /// <para>The daily picker takes the LEAST used first. Picking at random instead lets
    /// the five videos of one day repeat each other's pictures while older ones are never
    /// chosen at all.</para>
    /// </summary>
    public int UseCount { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}
