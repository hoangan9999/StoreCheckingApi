using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Repositories;

public sealed class GeneratedPostRepository(AppDbContext db) : IGeneratedPostRepository
{
    public async Task<(int Total, IReadOnlyList<GeneratedPost> Items)> ListAsync(
        DateOnly? day, int skip, int take, CancellationToken ct = default)
    {
        var q = db.GeneratedPosts.AsQueryable();
        if (day is { } d) q = q.Where(x => x.BatchDay == d);

        var total = await q.CountAsync(ct);

        // Id breaks ties: five posts written in one batch share created_at to the
        // microsecond, and an undefined order between them makes paging drop rows.
        var items = await q
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take).ToListAsync(ct);

        return (total, items);
    }

    public Task<int> CountForDayAsync(DateOnly day, CancellationToken ct = default) =>
        db.GeneratedPosts.CountAsync(x => x.BatchDay == day, ct);

    public Task<int> CountPostedForDayAsync(DateOnly day, CancellationToken ct = default) =>
        db.GeneratedPosts.CountAsync(x => x.BatchDay == day && x.PostedAt != null, ct);

    /// <summary>Bài cũ nhất trong ngày chưa đăng — cái tiếp theo tới lượt lên Fanpage.</summary>
    public Task<GeneratedPost?> NextUnpostedAsync(DateOnly day, CancellationToken ct = default) =>
        db.GeneratedPosts
            .Where(x => x.BatchDay == day && x.PostedAt == null && x.Status == PostStatus.Ready)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

    public Task<GeneratedPost?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.GeneratedPosts.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<IReadOnlyList<GeneratedPost>> ListOlderThanAsync(
        DateOnly cutoff, CancellationToken ct = default) =>
        db.GeneratedPosts.Where(x => x.BatchDay < cutoff).ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<GeneratedPost>)t.Result, ct);

    public void Add(GeneratedPost row) => db.GeneratedPosts.Add(row);
    public void RemoveRange(IEnumerable<GeneratedPost> rows) => db.GeneratedPosts.RemoveRange(rows);
}
