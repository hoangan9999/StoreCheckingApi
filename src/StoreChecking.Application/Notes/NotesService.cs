using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Notes;

/// <summary>
/// Quick notes — the list under the Marketing tab.
/// <para>Deliberately permissive about what a note may contain. On Supabase an empty note
/// was allowed (<c>content</c> defaults to an empty string) and the client renders one
/// harmlessly, so adding a rule here would reject something users can already have.</para>
/// </summary>
public sealed class NotesService(
    INoteRepository notes, ICurrentUser user, IUnitOfWork uow, IMediaStorage storage)
{
    private static NoteDto ToDto(Note r) =>
        new(r.Id, r.Title, r.Content, r.Images, r.CreatedAt, r.UpdatedAt);

    /// <summary>A blank heading is stored as null, so the client's "has a title" check is
    /// not fooled by a string of spaces.</summary>
    private static string? CleanTitle(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    public async Task<IReadOnlyList<NoteDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await notes.ListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<NoteDto> AddAsync(SaveNoteRequest body, CancellationToken ct = default)
    {
        var existing = await notes.ListAsync(ct);

        var row = new Note
        {
            UserId = user.Id,
            Title = CleanTitle(body.Title),
            // Content is NOT trimmed: notes exist to be copied verbatim, and a template's
            // own leading blank line or indentation is part of what the user saved.
            Content = body.Content ?? "",
            // One above the current top, so a new note still appears first — the habit
            // from before the list could be arranged by hand. Nothing else has to move.
            Position = existing.Count == 0 ? 0 : existing.Min(x => x.Position) - 1,
        };
        notes.Add(row);
        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <summary>Lay the notes out in the order given, and return the list as it now stands.</summary>
    /// <remarks>
    /// The whole list comes back rather than a bare 204, so a note added on another device
    /// while this one was being dragged shows up straight away instead of only after the
    /// next reload. Those unnamed notes keep their relative order and sit after the ones
    /// the client did name — it cannot have meant anything about a note it never saw.
    /// </remarks>
    public async Task<IReadOnlyList<NoteDto>> ReorderAsync(
        IReadOnlyList<Guid>? ids, CancellationToken ct = default)
    {
        var rows = await notes.ListAsync(ct);
        if (rows.Count == 0) return [];

        var byId = rows.ToDictionary(x => x.Id);
        var ordered = new List<Note>();

        foreach (var id in ids ?? [])
        {
            // A repeated id, or one belonging to somebody else's note (the query filter
            // never returned it), is simply not an instruction about this list.
            if (byId.Remove(id, out var row)) ordered.Add(row);
        }

        ordered.AddRange(rows.Where(x => byId.ContainsKey(x.Id)));
        for (var i = 0; i < ordered.Count; i++) ordered[i].Position = i;

        // UpdatedAt is deliberately left alone: moving a note is not editing it, and the
        // listing still falls back to that timestamp wherever positions tie.
        await uow.SaveChangesAsync(ct);
        return ordered.Select(ToDto).ToList();
    }

    /// <returns><c>null</c> when no such note belongs to the current user.</returns>
    /// <summary>Đính một ảnh vào ghi chú, trả về ghi chú đã cập nhật.</summary>
    public async Task<NoteDto?> AddImageAsync(
        Guid id, Stream content, string extension, CancellationToken ct = default)
    {
        var row = await notes.FindAsync(id, ct);
        if (row is null) return null;

        var filename = await storage.SaveNoteImageAsync(content, extension, ct);

        // Gán mảng mới thay vì sửa tại chỗ: EF không nhận ra một mảng bị đổi phần tử bên
        // trong, nên sửa tại chỗ sẽ lưu mà không có gì thay đổi.
        row.Images = [.. row.Images, filename];
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);

        return ToDto(row);
    }

    /// <summary>Gỡ một ảnh khỏi ghi chú và xoá file.</summary>
    public async Task<NoteDto?> RemoveImageAsync(Guid id, string filename, CancellationToken ct = default)
    {
        var row = await notes.FindAsync(id, ct);
        if (row is null) return null;
        if (!row.Images.Contains(filename)) return ToDto(row);

        row.Images = [.. row.Images.Where(x => x != filename)];
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);

        // Xoá file sau khi dòng đã lưu: ngược lại thì lưu hỏng sẽ để lại ghi chú trỏ vào
        // một tấm ảnh không còn.
        storage.DeleteNoteImage(filename);
        return ToDto(row);
    }

    public Stream? OpenImage(string filename) => storage.OpenNoteImage(filename);

    public async Task<NoteDto?> UpdateAsync(Guid id, SaveNoteRequest body, CancellationToken ct = default)
    {
        var row = await notes.FindAsync(id, ct);
        if (row is null) return null;

        row.Title = CleanTitle(body.Title);
        row.Content = body.Content ?? "";
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <returns><c>false</c> when no such note belongs to the current user.</returns>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await notes.FindAsync(id, ct);
        if (row is null) return false;

        var images = row.Images;

        notes.Remove(row);
        await uow.SaveChangesAsync(ct);

        // Ảnh đi theo ghi chú. Không xoá thì chúng nằm lại trên đĩa mãi mà không còn chỗ
        // nào hiển thị để mà biết chúng tồn tại.
        foreach (var f in images) storage.DeleteNoteImage(f);
        return true;
    }
}
