using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Abstractions;

/// <summary>Quick notes, most recently touched first.</summary>
public interface INoteRepository
{
    Task<IReadOnlyList<Note>> ListAsync(CancellationToken ct = default);
    Task<Note?> FindAsync(Guid id, CancellationToken ct = default);
    void Add(Note row);
    void Remove(Note row);
}
