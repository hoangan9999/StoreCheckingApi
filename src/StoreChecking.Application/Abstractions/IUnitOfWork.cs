namespace StoreChecking.Application.Abstractions;

/// <summary>
/// Commits everything the repositories have staged, in one transaction.
/// <para>Repositories deliberately do NOT save. Keeping the commit here is what lets an
/// application service change several things and have them land together or not at all —
/// and it keeps "when do we write" a decision of the use case rather than of whichever
/// repository happened to be called last.</para>
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
