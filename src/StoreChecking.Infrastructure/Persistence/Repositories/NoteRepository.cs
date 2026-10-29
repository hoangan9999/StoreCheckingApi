using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Repositories;

public sealed class NoteRepository(AppDbContext db) : INoteRepository
{
    // The user's own arrangement first; most recently touched decides between notes that
    // have never been dragged apart. Id breaks the last tie: two notes saved in the same
    // transaction share updated_at exactly, and an undefined order between them makes the
    // list jump around between reloads.
    public async Task<IReadOnlyList<Note>> ListAsync(CancellationToken ct = default) =>
        await db.Notes
            .OrderBy(x => x.Position)
            .ThenByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public Task<Note?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.Notes.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(Note row) => db.Notes.Add(row);
    public void Remove(Note row) => db.Notes.Remove(row);
}
