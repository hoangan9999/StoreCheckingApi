using StoreChecking.Application.Abstractions;

namespace StoreChecking.Api.Auth;

/// <summary>
/// The current user, normally read from the request token.
///
/// <para>Background work has no request and so no token, but it still has to be scoped to
/// somebody or every query would come back empty. <see cref="RunAs"/> supplies that owner
/// for one DI scope only — the scope the background job created itself. An HTTP request
/// never calls it, so a request can never choose whose data it sees.</para>
/// </summary>
public sealed class ScopeUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private readonly HttpCurrentUser _fromRequest = new(accessor);
    private Guid? _impersonated;

    public Guid Id => _impersonated ?? _fromRequest.Id;
    public bool IsAnonymous => Id == Guid.Empty;

    /// <summary>Only for work with no request behind it. Never call this from a controller.</summary>
    public void RunAs(Guid userId) => _impersonated = userId;
}
