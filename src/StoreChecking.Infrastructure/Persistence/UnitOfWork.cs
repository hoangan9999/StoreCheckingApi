using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Common;

namespace StoreChecking.Infrastructure.Persistence;

/// <summary>
/// EF Core already is a unit of work — the change tracker collects the edits and
/// SaveChanges writes them in one transaction. This type exists so the application layer
/// can commit without referencing EF, and so the one place every write passes through can
/// translate the database's own refusals into something the user can read.
/// </summary>
public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    /// <summary>
    /// PostgreSQL's SQLSTATE for a plpgsql <c>raise exception</c>.
    /// </summary>
    private const string RaisedByTrigger = "P0001";

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: RaisedByTrigger } pg)
        {
            // check_stock() and check_damage() live in the database on purpose: they hold
            // even when a bug in the application would let an oversell through. But their
            // message is already written for a person — "Không đủ tồn kho: còn 3, yêu cầu
            // bán 5" — so it deserves to reach one, as a 400 rather than a 500 with a
            // stack trace.
            //
            // The Application layer checks first so this is normally unreachable. It stays
            // because "normally" is doing a lot of work in that sentence: two sales of the
            // last item at the same moment both pass their check and only the database can
            // separate them.
            throw new ValidationException(pg.MessageText);
        }
    }
}
