using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StoreChecking.ContractTests;

/// <summary>
/// Stands in for Supabase's JWT during tests: the caller names itself with an
/// <c>X-Test-Sub</c> header carrying a user id, and this hands the app a principal with
/// that <c>sub</c> claim.
/// <para>Signing real tokens would mean either shipping a private key or calling
/// Supabase from CI. Neither is worth it: what the contract tests care about is what the
/// app DOES with an identity, not how the identity was proven. Sending no header leaves
/// the request anonymous, which is how the 401 cases are tested.</para>
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string SubHeader = "X-Test-Sub";
    public const string EmailHeader = "X-Test-Email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubHeader, out var sub) || string.IsNullOrWhiteSpace(sub))
            return Task.FromResult(AuthenticateResult.NoResult());

        var email = Request.Headers.TryGetValue(EmailHeader, out var e) && !string.IsNullOrWhiteSpace(e)
            ? e.ToString()
            : "test@example.com";

        // Claim names match what Supabase issues and what CurrentUser reads. The app sets
        // MapInboundClaims = false precisely so `sub` stays `sub`.
        var identity = new ClaimsIdentity(
            [new Claim("sub", sub.ToString()), new Claim("email", email)],
            SchemeName,
            nameType: "sub",
            roleType: ClaimTypes.Role);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
