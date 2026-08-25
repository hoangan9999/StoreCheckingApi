using System.Security.Claims;

namespace StoreChecking.Api.Auth;

/// <summary>
/// The user behind the current request, taken from the JWT `sub` claim.
/// <para>This is what REPLACES Supabase row level security. On Supabase the database
/// itself refuses to return other people's rows even if a query forgets its filter.
/// There is no such safety net here, so this Id feeds EF Core's global query filter
/// (see AppDbContext) and every query filters by owner automatically.</para>
/// <para>NEVER take the user id from a request body or query string.</para>
/// </summary>
public sealed class CurrentUser
{
    public Guid Id { get; }

    public CurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;
        // Supabase puts the user id in `sub`. ASP.NET may map `sub` to NameIdentifier,
        // so try both depending on how MapInboundClaims is configured.
        var raw = principal?.FindFirstValue("sub")
                  ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        Id = Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    /// <summary>Not signed in, or the token carries no usable `sub`.</summary>
    public bool IsAnonymous => Id == Guid.Empty;
}
