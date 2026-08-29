using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Common;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Packing;

/// <summary>
/// The packing video log: which order was filmed, how many times, and under what file name
/// on the NAS.
/// </summary>
public sealed class PackingService(
    IPackingVideoRepository videos,
    ICurrentUser user,
    IUnitOfWork uow)
{
    /// <summary>
    /// Upper bound on one listing. Far larger than the other modules because this list is
    /// fetched whole for two jobs — syncing against what is on the NAS, and cleaning up old
    /// recordings — and a silent truncation there would make files look unlogged and get
    /// imported a second time.
    /// </summary>
    public const int MaxListSize = 10_000;

    private const int DefaultListSize = 100;

    private static PackingVideoDto ToDto(PackingVideo r) =>
        new(r.Id, r.OrderCode, r.Seq, r.Note, r.Filename, r.RecordedAt);

    public async Task<IReadOnlyList<PackingVideoDto>> ListAsync(
        string? search, int? limit, CancellationToken ct = default)
    {
        var take = limit is null or < 1 ? DefaultListSize : Math.Min(limit.Value, MaxListSize);
        var rows = await videos.SearchAsync(search, take, ct);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// Logs one recording and hands back the name its file must be uploaded under.
    /// <para>The server decides the name because only the server knows what exists already.
    /// The client then uploads the video to the NAS under exactly that name, which is what
    /// ties the two together.</para>
    /// </summary>
    public async Task<SavedPackingDto> SaveAsync(SavePackingRequest body, CancellationToken ct = default)
    {
        var code = (body.OrderCode ?? "").Trim();
        if (code.Length == 0) throw new ValidationException("Thiếu mã đơn.");

        // Defaults to mp4, matching the client. Stripped of a leading dot and of anything
        // that has no business in a file name.
        var ext = (body.Ext ?? "").Trim().TrimStart('.');
        if (ext.Length == 0) ext = "mp4";
        if (!ext.All(char.IsLetterOrDigit)) throw new ValidationException("Đuôi file không hợp lệ.");

        var seq = await videos.NextSeqAsync(code, ct);
        var filename = $"{code}_{seq}.{ext}";

        videos.Add(new PackingVideo
        {
            UserId = user.Id,
            OrderCode = code,
            Seq = seq,
            Filename = filename,
            RecordedAt = DateTimeOffset.UtcNow,
        });
        await uow.SaveChangesAsync(ct);

        return new SavedPackingDto(seq, filename);
    }

    /// <returns><c>false</c> when no such recording belongs to the current user.</returns>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await videos.FindAsync(id, ct);
        if (row is null) return false;

        videos.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Every file name already logged, so the client can tell what on the NAS is new.</summary>
    public Task<IReadOnlyList<string>> FilenamesAsync(CancellationToken ct = default) =>
        videos.FilenamesAsync(ct);

    /// <summary>
    /// Adds rows for videos found on the NAS that were never logged — after a reinstall, or
    /// for files uploaded outside the app.
    /// <para>Skips any file name already present rather than trusting the caller to have
    /// filtered. The client does filter, but a retry after a half-finished import would
    /// otherwise log the same file twice, and there is no unique index to catch it.</para>
    /// </summary>
    public async Task<ImportPackingResult> ImportAsync(
        ImportPackingRequest body, CancellationToken ct = default)
    {
        var items = body.Items ?? [];
        if (items.Count == 0) return new ImportPackingResult(0, 0);

        var known = (await videos.FilenamesAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var fresh = new List<PackingVideo>();
        var skipped = 0;

        foreach (var r in items)
        {
            var code = (r.OrderCode ?? "").Trim();
            var name = (r.Filename ?? "").Trim();
            if (code.Length == 0 || name.Length == 0) throw new ValidationException("Thiếu mã đơn hoặc tên file.");

            // `known` is added to as we go, so duplicates inside one request are caught too.
            if (!known.Add(name)) { skipped++; continue; }

            fresh.Add(new PackingVideo
            {
                UserId = user.Id,
                OrderCode = code,
                Seq = r.Seq,
                Filename = name,
                RecordedAt = r.RecordedAt,
            });
        }

        if (fresh.Count > 0)
        {
            videos.AddRange(fresh);
            await uow.SaveChangesAsync(ct);
        }

        return new ImportPackingResult(fresh.Count, skipped);
    }
}
