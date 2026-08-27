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

    private static readonly Lazy<string?> Probe = new(() =>
    {
        try
        {
            var b = new NpgsqlConnectionStringBuilder(ConnectionString) { Timeout = 3 };
            using var c = new NpgsqlConnection(b.ConnectionString);
            c.Open();
            ApplySchema(c);
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
    /// Runs the same db/*.sql the NAS runs. Those files are written with
    /// `create table if not exists`, so re-running them on a warm database is a no-op.
    /// </summary>
    private static void ApplySchema(NpgsqlConnection open)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "db"), "*.sql").OrderBy(f => f))
        {
            using var cmd = new NpgsqlCommand(File.ReadAllText(file), open);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Walks up from the test binaries until the repository root is found. Hard-coding
    /// "../../../.." breaks the moment the target framework or configuration changes.
    /// </summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StoreChecking.sln")))
            dir = dir.Parent;

        return dir?.FullName
               ?? throw new InvalidOperationException("Không tìm thấy gốc kho (StoreChecking.sln).");
    }
}
