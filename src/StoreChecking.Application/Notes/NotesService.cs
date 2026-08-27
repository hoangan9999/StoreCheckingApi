using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Notes;

/// <summary>
/// Quick notes — the list under the Marketing tab.
/// <para>Deliberately permissive about what a note may contain. On Supabase an empty note
/// was allowed (<c>content</c> defaults to an empty string) and the client renders one
/// harmlessly, so adding a rule here would reject something users can already have.</para>
/// </summary>
public sealed class NotesService(INoteRepository notes, ICurrentUser user, IUnitOfWork uow)
{
    private static NoteDto ToDto(Note r) => new(r.Id, r.Title, r.Content, r.CreatedAt, r.UpdatedAt);

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
        var row = new Note
        {
            UserId = user.Id,
            Title = CleanTitle(body.Title),
            // Content is NOT trimmed: notes exist to be copied verbatim, and a template's
            // own leading blank line or indentation is part of what the user saved.
            Content = body.Content ?? "",
        };
        notes.Add(row);
        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <returns><c>null</c> when no such note belongs to the current user.</returns>
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

        notes.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }
}
