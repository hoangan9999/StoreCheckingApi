using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace StoreChecking.Infrastructure.Persistence;

/// <summary>
/// Applies the db/*.sql files at startup, once each, in filename order.
/// <para>Before this existed, every new table meant uploading a .sql file to the NAS
/// through File Station and running psql by hand in a container terminal — four manual
/// steps per module, repeated for every one of them, and only doable from home. Now the
/// files ride along inside the image and the API applies whatever is missing when it
/// boots. Deploying the code deploys the schema.</para>
/// <para>Safe to run against a database that already has the tables: every file is written
/// with <c>create ... if not exists</c>, which is also what makes the very first run
/// harmless on the NAS, where 001 and 002 were applied by hand long ago.</para>
/// <para>A failure here stops the application from starting. That is deliberate — an API
/// serving requests against a half-migrated schema fails in confusing ways much later,
/// while a container that will not start is noticed within a minute by
/// <c>tools/deploy.ps1</c>, which waits for /health to report the new commit.</para>
/// </summary>
public sealed class SchemaMigrator(string connectionString, ILogger<SchemaMigrator> log)
{
    private const string HistoryTable = "schema_history";

    /// <summary>
    /// Guards against two instances migrating at the same time. An arbitrary constant —
    /// it only has to be the same number in every process that touches this database.
    /// </summary>
    private const long AdvisoryLockKey = 8140_2026;

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        var scripts = LoadScripts();
        if (scripts.Count == 0)
        {
            log.LogWarning("Không tìm thấy file schema nào nhúng trong ảnh — bỏ qua bước nạp schema.");
            return;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // Held for the whole run and released when the connection closes. Without it two
        // containers starting together could both try to create the same table.
        await Exec(conn, $"select pg_advisory_lock({AdvisoryLockKey})", ct);

        await Exec(conn, $"""
            create table if not exists public.{HistoryTable} (
              filename   text primary key,
              checksum   text        not null,
              applied_at timestamptz not null default now()
            )
            """, ct);

        var applied = await LoadHistory(conn, ct);
        var ran = 0;

        foreach (var (name, sql) in scripts)
        {
            var checksum = Checksum(sql);

            if (applied.TryGetValue(name, out var previous))
            {
                if (previous != checksum)
                {
                    // Editing a file that already ran means the database and the repository
                    // disagree, and nothing downstream would notice. Say so loudly instead.
                    throw new InvalidOperationException(
                        $"File schema '{name}' đã chạy rồi nhưng nội dung nay khác đi. " +
                        "Đừng sửa file đã áp dụng — tạo file mới với số thứ tự kế tiếp.");
                }
                continue;
            }

            log.LogInformation("Đang nạp schema {File}", name);

            await using (var tx = await conn.BeginTransactionAsync(ct))
            {
                await using (var cmd = new NpgsqlCommand(sql, conn, tx)) await cmd.ExecuteNonQueryAsync(ct);

                await using (var mark = new NpgsqlCommand(
                    $"insert into public.{HistoryTable} (filename, checksum) values (@f, @c)", conn, tx))
                {
                    mark.Parameters.AddWithValue("f", name);
                    mark.Parameters.AddWithValue("c", checksum);
                    await mark.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
            }

            ran++;
        }

        log.LogInformation("Schema: {Ran} file mới nạp, {Total} file tổng cộng.", ran, scripts.Count);
    }

    /// <summary>
    /// The .sql files, embedded by StoreChecking.Infrastructure.csproj and ordered by the
    /// numeric prefix in their names.
    /// </summary>
    private static List<(string Name, string Sql)> LoadScripts()
    {
        var asm = typeof(SchemaMigrator).Assembly;

        return asm.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => (Name: ShortName(n), Sql: Read(asm, n)))
            .ToList();
    }

    /// <summary>
    /// Drops the assembly and folder prefix so the history table holds something readable
    /// and stable, like <c>003-notes.sql</c>.
    /// </summary>
    private static string ShortName(string resourceName)
    {
        const string marker = ".Schema.";
        var at = resourceName.IndexOf(marker, StringComparison.Ordinal);
        return at < 0 ? resourceName : resourceName[(at + marker.Length)..];
    }

    private static string Read(Assembly asm, string resourceName)
    {
        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Checksum(string sql)
    {
        // Line endings are normalised first: git may hand out CRLF on one machine and LF on
        // another, and a checksum that changed with the checkout would look like tampering.
        var normalised = sql.Replace("\r\n", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }

    private static async Task<Dictionary<string, string>> LoadHistory(NpgsqlConnection conn, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var cmd = new NpgsqlCommand($"select filename, checksum from public.{HistoryTable}", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result[reader.GetString(0)] = reader.GetString(1);

        return result;
    }

    private static async Task Exec(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
