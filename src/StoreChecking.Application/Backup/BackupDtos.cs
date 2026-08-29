using System.Text.Json;

namespace StoreChecking.Application.Backup;

/// <summary>
/// Everything worth keeping, in the shape the existing backup files already use.
/// <para><c>Tables</c> maps a table name to its rows exactly as they sit in the database —
/// snake_case column names, every column, nothing renamed. That is deliberate: backup files
/// written before the migration have this shape, and a backup format that changes quietly
/// is a backup you cannot restore from.</para>
/// </summary>
public sealed record BackupDto(
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyDictionary<string, JsonElement> Tables);
