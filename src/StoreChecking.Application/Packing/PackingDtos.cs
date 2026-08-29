namespace StoreChecking.Application.Packing;

/// <summary>One packing recording as returned to the client.</summary>
public record PackingVideoDto(
    Guid Id, string OrderCode, int Seq, string? Note, string? Filename, DateTimeOffset RecordedAt);

/// <summary>
/// Log a new recording for an order. The sequence number and the file name are worked out
/// by the server, because only the server can see what already exists for that order.
/// </summary>
public record SavePackingRequest(string OrderCode, string? Ext);

/// <summary>
/// What the client needs in order to upload the file: the name to give it, and which
/// recording of that order it is.
/// </summary>
public record SavedPackingDto(int Seq, string Filename);

/// <summary>One row of a bulk import, read from what is actually on the NAS.</summary>
public record ImportPackingRow(string OrderCode, int Seq, string Filename, DateTimeOffset RecordedAt);

/// <summary>Bulk import request — files found on the NAS that have no row yet.</summary>
public record ImportPackingRequest(IReadOnlyList<ImportPackingRow> Items);

/// <summary>How the import went. <c>Skipped</c> counts rows whose file name was already logged.</summary>
public record ImportPackingResult(int Added, int Skipped);
