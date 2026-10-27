using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Backup;

namespace StoreChecking.Infrastructure.Persistence.Repositories;

/// <summary>
/// The one place in this folder that writes SQL by hand, and the only one that does not go
/// through EF's owner filter.
///
/// <para>Why: a backup has to contain EVERY column. Listing them in C# means the day a
/// column is added and the mapping is not updated, backups quietly start losing data — the
/// same failure that hid <c>sales.shipping_fee</c> until a data copy broke on it, except
/// here nothing would ever break to reveal it. <c>row_to_json</c> reads whatever the table
/// actually has.</para>
///
/// <para>The price is that the owner filter does not apply, so the <c>where user_id</c> is
/// written out explicitly below and pinned by an isolation test. The table name is
/// interpolated, which is safe only because it comes from
/// <see cref="BackupService.Tables"/> — a fixed list in code — and never from a request.
/// That is checked here as well rather than trusted.</para>
/// </summary>
public sealed class BackupRepository(AppDbContext db, ICurrentUser user) : IBackupRepository
{
    /// <summary>
    /// Tables owning per-user rows: every public table with a `user_id` column.
    /// <para>That column IS the definition — it is what makes a row belong to someone, and
    /// what the dump below filters on. `schema_history` has none and is correctly left out:
    /// it records which migrations ran, not anybody's data.</para>
    /// </summary>
    public async Task<IReadOnlyList<string>> TablesAsync(CancellationToken ct = default) =>
        await db.Database.SqlQueryRaw<string>(
            @"select c.table_name as ""Value""
              from information_schema.columns c
              join information_schema.tables t
                on t.table_schema = c.table_schema and t.table_name = c.table_name
              where c.table_schema = 'public'
                and c.column_name = 'user_id'
                and t.table_type = 'BASE TABLE'
              order by c.table_name")
            .ToListAsync(ct);

    public async Task<JsonElement> DumpAsync(string table, CancellationToken ct = default)
    {
        // The name is interpolated into SQL below, so it is checked against the catalogue
        // here rather than trusted. The shape check comes first: it is cheap, and it means
        // nothing strange ever reaches the catalogue query either.
        if (!System.Text.RegularExpressions.Regex.IsMatch(table, "^[a-z][a-z0-9_]*$"))
            throw new ArgumentException($"Tên bảng '{table}' không hợp lệ.", nameof(table));

        if (!(await TablesAsync(ct)).Contains(table))
            throw new ArgumentException($"'{table}' không phải bảng có dữ liệu theo người dùng.", nameof(table));

        // json_agg over the whole table gives one string back rather than one row per record,
        // and coalesce turns an empty table into [] instead of null. EF requires the scalar
        // column to be called Value. {0} is a real parameter placeholder, not interpolation.
        var sql =
            "select coalesce(json_agg(t), '[]'::json)::text as \"Value\" " +
            "from public." + table + " t " +
            "where t.user_id = {0}";

        var json = await db.Database
            .SqlQueryRaw<string>(sql, user.Id)
            .SingleAsync(ct);

        // Cloned so the JsonDocument can be disposed without the caller losing the data.
        using var parsed = JsonDocument.Parse(json);
        return parsed.RootElement.Clone();
    }
}
