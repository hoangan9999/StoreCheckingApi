using System.Text.Json;
using StoreChecking.Application.Abstractions;

namespace StoreChecking.Application.Backup;

/// <summary>
/// Builds the backup file's contents.
/// </summary>
public sealed class BackupService(IBackupRepository backup)
{
    /// <summary>
    /// The tables that go into a backup, in the order the previous implementation read them.
    /// <para>A FIXED list, not something a request can influence: it is interpolated into
    /// SQL, so anything else would be an injection hole.</para>
    /// <para>Deliberately the same eight as before and no more. notes, work_days,
    /// work_month_notes, english_words and speaking_saved were never in a backup and adding
    /// them here would quietly change what a backup file contains.</para>
    /// </summary>
    public static readonly string[] Tables =
    [
        "batches",
        "products",
        "sales",
        "product_damages",
        "expense_categories",
        "expenses",
        "monthly_income",
        "packing_videos",
    ];

    public async Task<BackupDto> DumpAsync(CancellationToken ct = default)
    {
        var tables = new Dictionary<string, JsonElement>(Tables.Length);
        var counts = new Dictionary<string, int>(Tables.Length);

        foreach (var name in Tables)
        {
            var rows = await backup.DumpAsync(name, ct);
            tables[name] = rows;
            counts[name] = rows.GetArrayLength();
        }

        return new BackupDto(counts, tables);
    }
}
