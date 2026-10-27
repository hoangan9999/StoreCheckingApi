using Npgsql;

namespace StoreChecking.ContractTests;

/// <summary>
/// The PostgreSQL instance the contract tests run against.
/// <para>These tests deliberately use a REAL PostgreSQL rather than an in-memory
/// provider. Half of what they protect only exists in the real database: the jsonb
/// column, ILIKE searching, gen_random_uuid() and now() defaults, and the exact
/// ordering that makes paging stable. An in-memory provider would pass while
/// production broke.</para>
/// <para>There is no PostgreSQL and no Docker on the development machine (a company
/// laptop), so these tests normally run in GitHub Actions against a postgres:16-alpine
/// service container. When no database can be reached they SKIP rather than fail, which
/// keeps `dotnet build` and IDE test discovery usable locally.</para>
/// </summary>
public static class TestDatabase
{
    /// <summary>Override with TEST_POSTGRES to point the suite at any other server.</summary>
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("TEST_POSTGRES")
        ?? "Host=localhost;Port=5432;Database=storechecking_test;Username=postgres;Password=postgres";

    // Only checks that a server answers. Creating the tables is NOT done here: the
    // application does it itself on startup (SchemaMigrator), and letting the tests run
    // that same code is the point — a separate copy of the schema-loading logic would
    // stop reflecting what production actually does.
    private static readonly Lazy<string?> Probe = new(() =>
    {
        try
        {
            var b = new NpgsqlConnectionStringBuilder(ConnectionString) { Timeout = 3 };
            using var c = new NpgsqlConnection(b.ConnectionString);
            c.Open();
            return null;
        }
        catch (Exception ex)
        {
            return $"Không nối được PostgreSQL cho test ({ex.GetType().Name}: {ex.Message}). " +
                   "Đặt TEST_POSTGRES để trỏ tới một server khác. Trên CI thì service container lo việc này.";
        }
    });

    public static bool IsAvailable => Probe.Value is null;
    public static string SkipReason => Probe.Value ?? "";

    /// <summary>
    /// Chạy một câu truy vấn trả về một cột chữ, dùng cho việc hỏi thẳng danh mục database.
    /// </summary>
    /// <remarks>
    /// Đi thẳng Npgsql chứ không qua API hay EF là có chủ đích: test nào muốn đối chiếu
    /// "code nghĩ có gì" với "database thật có gì" thì phải hỏi database bằng một đường
    /// khác hẳn đường đang kiểm tra. Hỏi qua chính lớp đó thì hai bên cùng sai vẫn khớp.
    /// </remarks>
    public static async Task<List<string>> QueryStringsAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }
}
