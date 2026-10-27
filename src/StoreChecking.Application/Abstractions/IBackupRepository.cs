using System.Text.Json;

namespace StoreChecking.Application.Abstractions;

/// <summary>
/// Reads whole tables as raw JSON, for the backup file.
/// </summary>
public interface IBackupRepository
{
    /// <summary>
    /// Every row of one table belonging to the current user, as a JSON array with the
    /// database's own column names.
    /// <para>The table name comes from a fixed list in the application layer and never from
    /// a request.</para>
    /// </summary>
    Task<JsonElement> DumpAsync(string table, CancellationToken ct = default);

    /// <summary>
    /// Every table that holds per-user rows, read from the database's own catalogue.
    /// <para>Asked rather than listed, for the same reason the dump uses json_agg instead of
    /// naming columns: a list in code is a list someone forgets to update, and a backup that
    /// quietly stops covering a new table gives no sign until the day it is needed.</para>
    /// </summary>
    Task<IReadOnlyList<string>> TablesAsync(CancellationToken ct = default);
}
