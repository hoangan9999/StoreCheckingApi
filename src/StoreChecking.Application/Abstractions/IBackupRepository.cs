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
}
