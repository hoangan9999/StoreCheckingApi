using StoreChecking.Application.Abstractions;

namespace StoreChecking.Infrastructure.Persistence;

/// <summary>
/// EF Core already is a unit of work — the change tracker collects the edits and
/// SaveChanges writes them in one transaction. This type exists only so the application
/// layer can commit without referencing EF.
/// </summary>
public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
