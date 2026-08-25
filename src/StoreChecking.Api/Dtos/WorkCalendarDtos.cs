namespace StoreChecking.Api.Dtos;

/// <summary>A day cell as returned to the client, using the field names Angular already expects.</summary>
public record WorkDayDto(Guid Id, string Day, string Note, string? Color);

/// <summary>New contents for a day cell.</summary>
public record SaveWorkDayRequest(string? Note, string? Color);

/// <summary>One month-note line as returned to the client.</summary>
public record MonthNoteDto(Guid Id, string Period, string Content, int Sort);

/// <summary>Create an empty month-note line; the text is filled in afterwards.</summary>
public record CreateMonthNoteRequest(string Period, int Sort);

/// <summary>Update the text of one month-note line.</summary>
public record UpdateMonthNoteRequest(string Content);
