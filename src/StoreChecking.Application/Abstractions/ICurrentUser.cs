namespace StoreChecking.Application.Abstractions;

/// <summary>
/// The user behind the current request.
/// <para>This is what REPLACES Supabase row level security. On Supabase the database
/// itself refused to return other people's rows even when a query forgot its filter.
/// There is no such net here: this Id feeds EF Core's global query filters, and every
/// query is scoped by owner automatically.</para>
/// <para>The value always comes from the token's <c>sub</c> claim. NEVER from a request
/// body, a query string, or a route parameter.</para>
/// </summary>
public interface ICurrentUser
{
    Guid Id { get; }

    /// <summary>Not signed in, or the token carries no usable <c>sub</c>.</summary>
    bool IsAnonymous { get; }
}
