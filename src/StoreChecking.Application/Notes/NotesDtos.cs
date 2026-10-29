namespace StoreChecking.Application.Notes;

/// <summary>A quick note as returned to the client.</summary>
public record NoteDto(
    Guid Id, string? Title, string Content, IReadOnlyList<string> Images,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Create or replace the contents of a note.</summary>
public record SaveNoteRequest(string? Title, string? Content);

/// <summary>
/// The note ids in the order the user dragged them into, top first.
/// <para>Ids rather than positions: the client knows the order it is looking at, and
/// nothing else about it. Sending numbers would mean two sides agreeing on what the
/// numbers mean, which is one more thing to get out of step.</para>
/// </summary>
public record ReorderNotesRequest(IReadOnlyList<Guid>? Ids);
