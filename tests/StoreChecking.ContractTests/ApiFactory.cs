using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace StoreChecking.ContractTests;

/// <summary>
/// Boots the real application in memory, against the real PostgreSQL, with only
/// authentication swapped out.
/// <para>Everything else is left exactly as production runs it — routing, the query
/// filters that replace Supabase's row level security, JSON naming, the error middleware.
/// A test harness that rebuilt any of that would stop noticing when the real wiring
/// changed, which is the only reason these tests exist.</para>
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // Configuration MUST arrive as environment variables, and MUST be set before the host
    // is ever created.
    //
    // Program.cs reads its settings in the top-level statements, well before
    // builder.Build(). WebApplicationFactory's ConfigureAppConfiguration only takes effect
    // AT Build(), by which point Program.cs has already thrown
    // "Thiếu ConnectionStrings__Postgres". Environment variables are read by
    // WebApplication.CreateBuilder itself, so they are in place in time — and they are how
    // docker-compose configures the real deployment anyway.
    //
    // A static constructor runs before any instance of this class exists, which is early
    // enough: the host is only built when a test first asks for a client.
    static ApiFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", TestDatabase.ConnectionString);

        // Never contacted: the JwtBearer scheme is registered but the default scheme is
        // overridden below, so no metadata is ever fetched. The value still has to satisfy
        // the startup validation in Program.cs.
        Environment.SetEnvironmentVariable("Auth__SupabaseUrl", "https://test-project.supabase.co");
        Environment.SetEnvironmentVariable("Cors__Origins", "https://example.test");
        Environment.SetEnvironmentVariable("Swagger__Enabled", "false");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        // Ảnh và video ghi vào thư mục tạm, KHÔNG phải Media:Root thật.
        //
        // Mặc định là /data/media, hợp với volume trong Docker và tạo được trên Windows
        // (thành C:\data\media). Trên Linux thì không: tài khoản chạy CI không có quyền
        // tạo /data, DiskMediaStorage ném ngay trong hàm dựng, và vì NotesService nhận
        // IMediaStorage nên MỌI test ghi chú đổ theo. Đã dựng lại đúng cảnh đó trong một
        // container Linux chạy bằng uid 1001: "mkdir: cannot create directory '/data'".
        MediaRoot = Path.Combine(Path.GetTempPath(), "storechecking-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("Media__Root", MediaRoot);

        // Không có khoá thì không đăng gì cả — nhưng nói rõ ra ở đây, để một cái máy có
        // sẵn biến môi trường FB không biến test thành lần đăng thật lên Fanpage.
        Environment.SetEnvironmentVariable("Facebook__PageId", "");
        Environment.SetEnvironmentVariable("Facebook__AccessToken", "");
        Environment.SetEnvironmentVariable("Facebook__AutoPost", "false");
    }

    /// <summary>Thư mục ảnh/video của lần chạy test này. Xoá khi chạy xong.</summary>
    private static string MediaRoot { get; set; } = "";

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && MediaRoot.Length > 0)
            try { Directory.Delete(MediaRoot, recursive: true); } catch { /* thư mục tạm */ }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Unlike configuration, service registration DOES arrive in time: these callbacks
        // run during Build(), after Program.cs has registered its own services, so this
        // last AddAuthentication call is the one that decides the default scheme.
        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>A client that speaks for one specific user id.</summary>
    public HttpClient ClientFor(Guid userId, string? email = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, userId.ToString());
        if (email is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        return client;
    }

    /// <summary>A client carrying no identity at all, for the 401 cases.</summary>
    public HttpClient AnonymousClient() => CreateClient();
}

/// <summary>
/// One application instance shared by every test class. Booting the host per class would
/// multiply startup cost for no benefit: tests isolate themselves by using a fresh user
/// id, which the global query filters already keep apart.
/// </summary>
[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
