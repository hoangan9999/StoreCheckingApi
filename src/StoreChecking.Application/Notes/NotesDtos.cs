namespace StoreChecking.Application.Notes;

/// <summary>A quick note as returned to the client.</summary>
public record NoteDto(Guid Id, string? Title, string Content, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Create or replace the contents of a note.</summary>
public record SaveNoteRequest(string? Title, string? Content);
