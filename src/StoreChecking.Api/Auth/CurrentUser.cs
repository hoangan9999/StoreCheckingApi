using System.Security.Claims;
using StoreChecking.Application.Abstractions;

namespace StoreChecking.Api.Auth;

/// <summary>
/// Reads the caller's identity out of the request's token.
/// <para>Lives in the API project because HttpContext is a web concern: Infrastructure and
/// Application only ever see <see cref="ICurrentUser"/>, which is what lets the whole data
/// layer be exercised in tests without a web request.</para>
/// </summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    public Guid Id { get; }

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;

        // Supabase puts the user id in `sub`. ASP.NET may map `sub` to NameIdentifier,
        // so try both depending on how MapInboundClaims is configured.
        var raw = principal?.FindFirstValue("sub")
                  ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        Id = Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    /// <summary>Not signed in, or the token carries no usable <c>sub</c>.</summary>
    public bool IsAnonymous => Id == Guid.Empty;
}
