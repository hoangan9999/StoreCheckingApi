using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Repositories;

public sealed class EnglishWordRepository(AppDbContext db) : IEnglishWordRepository
{
    public Task<int> CountAsync(CancellationToken ct = default) => db.EnglishWords.CountAsync(ct);

    // Ordering falls back to Id on purpose. created_at alone is not unique — rows written
    // in one transaction share it exactly, because now() is transaction time — and ties
    // make the order undefined between queries, so page 2 repeats rows from page 1.
    public async Task<IReadOnlyList<EnglishWord>> ListAsync(int skip, int take, CancellationToken ct = default) =>
        await db.EnglishWords
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

    public Task<EnglishWord?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.EnglishWords.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(EnglishWord row) => db.EnglishWords.Add(row);
    public void Remove(EnglishWord row) => db.EnglishWords.Remove(row);
}

public sealed class SavedSentenceRepository(AppDbContext db) : ISavedSentenceRepository
{
    public async Task<(int Total, IReadOnlyList<SavedSentence> Items)> SearchAsync(
        string? query, int skip, int take, CancellationToken ct = default)
    {
        var rows = db.SavedSentences.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim();
            // ILike, not Contains: the search has to ignore case, and it looks in the note
            // as well as the sentence itself.
            rows = rows.Where(x => EF.Functions.ILike(x.Text, $"%{needle}%")
                                || EF.Functions.ILike(x.Note, $"%{needle}%"));
        }

        var total = await rows.CountAsync(ct);
        var page = await rows
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

        return (total, page);
    }

    public Task<SavedSentence?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.SavedSentences.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<SavedSentence?> FindByTextAsync(string text, CancellationToken ct = default) =>
        db.SavedSentences.FirstOrDefaultAsync(x => x.Text == text, ct);

    public void Add(SavedSentence row) => db.SavedSentences.Add(row);
    public void Remove(SavedSentence row) => db.SavedSentences.Remove(row);
}
