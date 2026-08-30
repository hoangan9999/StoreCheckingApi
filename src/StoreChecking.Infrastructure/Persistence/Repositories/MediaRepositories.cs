using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Repositories;

public sealed class MediaImageRepository(AppDbContext db) : IMediaImageRepository
{
    /// <summary>
    /// How much wider than needed to look before shuffling.
    /// <para>The ask was "random", but random across the whole album lets one day's five
    /// videos repeat each other's pictures while older ones are never touched. Taking the
    /// least-used slice and shuffling INSIDE it gives both: every picture gets its turn,
    /// and no two videos line up the same way.</para>
    /// </summary>
    private const int PoolFactor = 3;

    public async Task<(int Total, IReadOnlyList<MediaImage> Items)> ListAsync(
        DateOnly? day, int skip, int take, CancellationToken ct = default)
    {
        var q = Scoped(day);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(x => x.UploadedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

        return (total, items);
    }

    public async Task<IReadOnlyList<(DateOnly Day, int Count)>> CountByDayAsync(
        CancellationToken ct = default)
    {
        var rows = await db.MediaImages
            .GroupBy(x => x.UploadedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Day)
            .ToListAsync(ct);

        return rows.Select(r => (DateOnly.FromDateTime(r.Day), r.Count)).ToList();
    }

    public async Task<IReadOnlyList<MediaImage>> PickLeastUsedAsync(
        int count, CancellationToken ct = default)
    {
        if (count < 1) return [];

        var pool = await db.MediaImages
            .OrderBy(x => x.UseCount)
            .ThenBy(x => x.LastUsedAt)
            .ThenBy(x => x.UploadedAt)
            .Take(count * PoolFactor)
            .ToListAsync(ct);

        // Shuffled here rather than with ORDER BY random(): the slice is small, and letting
        // the database sort the whole album randomly costs a full scan every single call.
        return pool.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
    }

    public Task<MediaImage?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.MediaImages.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<int> CountAsync(CancellationToken ct = default) => db.MediaImages.CountAsync(ct);

    public async Task<Guid?> FindOwnerAsync(CancellationToken ct = default)
    {
        // IgnoreQueryFilters vì lúc này chưa biết chủ sở hữu là ai — chính nó là thứ đang
        // đi tìm. Chỉ trả về một id, không trả về dòng dữ liệu nào.
        var rows = await db.MediaImages.IgnoreQueryFilters()
            .GroupBy(x => x.UserId)
            .Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(1)
            .ToListAsync(ct);

        return rows.Count > 0 ? rows[0].Key : null;
    }

    public void Add(MediaImage row) => db.MediaImages.Add(row);
    public void Remove(MediaImage row) => db.MediaImages.Remove(row);

    private IQueryable<MediaImage> Scoped(DateOnly? day)
    {
        var q = db.MediaImages.AsQueryable();
        if (day is null) return q;

        var from = day.Value.ToDateTime(TimeOnly.MinValue);
        return q.Where(x => x.UploadedAt >= from && x.UploadedAt < from.AddDays(1));
    }
}

public sealed class GeneratedVideoRepository(AppDbContext db) : IGeneratedVideoRepository
{
    public async Task<(int Total, IReadOnlyList<GeneratedVideo> Items)> ListAsync(
        DateOnly? day, int skip, int take, CancellationToken ct = default)
    {
        var q = db.GeneratedVideos.AsQueryable();
        if (day is not null) q = q.Where(x => x.BatchDay == day.Value);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

        return (total, items);
    }

    public async Task<IReadOnlyList<(DateOnly Day, int Count)>> CountByDayAsync(
        CancellationToken ct = default)
    {
        var rows = await db.GeneratedVideos
            .GroupBy(x => x.BatchDay)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Day)
            .ToListAsync(ct);

        return rows.Select(r => (r.Day, r.Count)).ToList();
    }

    // Counts everything except failures on purpose: a video that broke should be retried,
    // not silently counted towards the day's five.
    public Task<int> CountForDayAsync(DateOnly day, CancellationToken ct = default) =>
        db.GeneratedVideos.CountAsync(x => x.BatchDay == day && x.Status != VideoStatus.Error, ct);

    public Task<GeneratedVideo?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.GeneratedVideos.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(GeneratedVideo row) => db.GeneratedVideos.Add(row);
    public void Remove(GeneratedVideo row) => db.GeneratedVideos.Remove(row);
}
