using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Repositories;

public sealed class PackingVideoRepository(AppDbContext db) : IPackingVideoRepository
{
    public async Task<IReadOnlyList<PackingVideo>> SearchAsync(
        string? query, int take, CancellationToken ct = default)
    {
        var rows = db.PackingVideos.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim();
            rows = rows.Where(x => EF.Functions.ILike(x.OrderCode, $"%{needle}%"));
        }

        // Id breaks ties: a bulk import writes many rows in one transaction, and several
        // videos of one order can share a recorded_at down to the second.
        return await rows
            .OrderByDescending(x => x.RecordedAt).ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> NextSeqAsync(string orderCode, CancellationToken ct = default)
    {
        // MAX, not COUNT. Deleting the middle recording of an order lowers the count, and
        // the next upload would then be handed a file name that already exists on the NAS.
        var highest = await db.PackingVideos
            .Where(x => x.OrderCode == orderCode)
            .Select(x => (int?)x.Seq)
            .MaxAsync(ct);

        return (highest ?? 0) + 1;
    }

    public Task<PackingVideo?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.PackingVideos.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<string>> FilenamesAsync(CancellationToken ct = default) =>
        await db.PackingVideos
            .Where(x => x.Filename != null)
            .Select(x => x.Filename!)
            .ToListAsync(ct);

    public void Add(PackingVideo row) => db.PackingVideos.Add(row);
    public void AddRange(IEnumerable<PackingVideo> rows) => db.PackingVideos.AddRange(rows);
    public void Remove(PackingVideo row) => db.PackingVideos.Remove(row);
}
