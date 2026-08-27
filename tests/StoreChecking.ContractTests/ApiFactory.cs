using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
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
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = TestDatabase.ConnectionString,
                // Never contacted: the JwtBearer scheme is registered but the default
                // scheme is overridden below, so no metadata is ever fetched. The value
                // still has to satisfy the startup validation in Program.cs.
                ["Auth:SupabaseUrl"] = "https://test-project.supabase.co",
                ["Cors:Origins"] = "https://example.test",
                ["Swagger:Enabled"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Runs after the app's own registrations, so this AddAuthentication call is
            // the one that decides the default scheme.
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
