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

public sealed class EnglishTopicRepository(AppDbContext db) : IEnglishTopicRepository
{
    // Position first, then newest: a topic nobody has reordered keeps position 0 alongside
    // its siblings, and the date is what separates them.
    public async Task<IReadOnlyList<EnglishTopic>> ListAsync(CancellationToken ct = default) =>
        await db.EnglishTopics
            .OrderBy(x => x.Position)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public Task<EnglishTopic?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.EnglishTopics.FirstOrDefaultAsync(x => x.Id == id, ct);

    // FirstOrDefault trên danh sách đã sắp, không phải Min: chưa có chủ đề nào thì câu
    // này trả null gọn gàng, khỏi phụ thuộc vào chuyện Min xử lý tập rỗng ra sao.
    public async Task<int?> MinPositionAsync(CancellationToken ct = default) =>
        await db.EnglishTopics
            .OrderBy(x => x.Position)
            .Select(x => (int?)x.Position)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<EnglishLessonLine>> LinesAsync(
        Guid topicId, CancellationToken ct = default) =>
        await db.EnglishLessonLines
            .Where(x => x.TopicId == topicId)
            .OrderBy(x => x.Position)
            .ToListAsync(ct);

    // One grouped query for every topic at once. Asking per topic would be one round trip
    // per row of the list screen, which is the shape that quietly gets slower every month.
    public async Task<IReadOnlyDictionary<Guid, (int Lines, int Phrases)>> CountsAsync(
        CancellationToken ct = default)
    {
        var rows = await db.EnglishLessonLines
            .GroupBy(x => new { x.TopicId, x.Kind })
            .Select(g => new { g.Key.TopicId, g.Key.Kind, Count = g.Count() })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.TopicId)
            .ToDictionary(
                g => g.Key,
                g => (
                    Lines: g.Where(r => r.Kind == EnglishLessonLine.Line).Sum(r => r.Count),
                    Phrases: g.Where(r => r.Kind == EnglishLessonLine.Phrase).Sum(r => r.Count)));
    }

    public void Add(EnglishTopic row) => db.EnglishTopics.Add(row);
    public void Remove(EnglishTopic row) => db.EnglishTopics.Remove(row);
    public void AddLines(IEnumerable<EnglishLessonLine> rows) => db.EnglishLessonLines.AddRange(rows);
    public void RemoveLines(IEnumerable<EnglishLessonLine> rows) => db.EnglishLessonLines.RemoveRange(rows);
}
