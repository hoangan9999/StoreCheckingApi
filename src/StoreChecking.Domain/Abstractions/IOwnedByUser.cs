namespace StoreChecking.Domain.Abstractions;

/// <summary>
/// A row that belongs to exactly one user.
/// <para>Implementing this is what gets an entity its owner filter: AppDbContext walks the
/// model and applies <c>e =&gt; e.UserId == currentUser</c> to every type that carries this
/// interface. Nobody has to remember a HasQueryFilter line, because there is no line to
/// forget — which matters a great deal with the rest of the app moving off Supabase and
/// its row level security.</para>
/// <para>A new table that does NOT implement this is either a genuine exception or a data
/// leak, and the contract tests treat it as the second until told otherwise.</para>
/// </summary>
public interface IOwnedByUser
{
    /// <summary>Owner. Comes from the token's <c>sub</c> claim, NEVER from the client.</summary>
    Guid UserId { get; set; }
}
