using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.WorkCalendar;

/// <summary>
/// Everything the work calendar can do, with no knowledge of HTTP.
/// <para>Request shape — is this a valid date, is <c>to</c> after <c>from</c> — is checked
/// at the API edge. What lives here is the behaviour: which rows exist, when a cell is
/// deleted rather than stored, and how a row becomes a DTO.</para>
/// </summary>
public sealed class WorkCalendarService(
    IWorkDayRepository days,
    IWorkMonthNoteRepository notes,
    ICurrentUser user,
    IUnitOfWork uow)
{
    /// <summary>Date format shared with the Angular client: 'YYYY-MM-DD'.</summary>
    public const string DayFormat = "yyyy-MM-dd";

    private static string S(DateOnly d) => d.ToString(DayFormat);

    private static WorkDayDto ToDto(WorkDay r) => new(r.Id, S(r.Day), r.Note, r.Color);
    private static MonthNoteDto ToDto(WorkMonthNote r) => new(r.Id, S(r.Period), r.Content, r.Sort);

    // ---------- Day cells ----------

    public async Task<IReadOnlyList<WorkDayDto>> ListDaysAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await days.ListRangeAsync(from, to, ct);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// Writes one day cell, or deletes it.
    /// <para>A cell with no note and no colour is DELETED rather than stored, so the table
    /// does not fill up with empty rows. This matches the Supabase behaviour the Angular
    /// app was built against, and it is destructive, so it is pinned by a contract test.</para>
    /// </summary>
    /// <returns>The saved cell, or <c>null</c> when the cell was emptied and removed.</returns>
    public async Task<WorkDayDto?> SaveDayAsync(DateOnly day, SaveWorkDayRequest body, CancellationToken ct = default)
    {
        var note = (body.Note ?? "").Trim();
        var color = string.IsNullOrWhiteSpace(body.Color) ? null : body.Color.Trim();
        var row = await days.FindByDayAsync(day, ct);

        if (note.Length == 0 && color is null)
        {
            if (row is not null) days.Remove(row);
            await uow.SaveChangesAsync(ct);
            return null;
        }

        if (row is null)
        {
            row = new WorkDay { UserId = user.Id, Day = day };
            days.Add(row);
        }

        // Stores the note as sent, not the trimmed copy: trimming decides only whether the
        // cell counts as empty.
        row.Note = body.Note ?? "";
        row.Color = color;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    // ---------- Month notes ----------

    public async Task<IReadOnlyList<MonthNoteDto>> ListNotesAsync(DateOnly period, CancellationToken ct = default)
    {
        var rows = await notes.ListByPeriodAsync(period, ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<MonthNoteDto> AddNoteAsync(DateOnly period, int sort, CancellationToken ct = default)
    {
        var row = new WorkMonthNote
        {
            UserId = user.Id,
            Period = period,
            Content = "",
            Sort = sort,
        };
        notes.Add(row);
        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <returns><c>null</c> when no such line belongs to the current user.</returns>
    public async Task<MonthNoteDto?> UpdateNoteAsync(Guid id, string? content, CancellationToken ct = default)
    {
        var row = await notes.FindAsync(id, ct);
        if (row is null) return null;

        row.Content = content ?? "";
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <returns><c>false</c> when no such line belongs to the current user.</returns>
    public async Task<bool> DeleteNoteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await notes.FindAsync(id, ct);
        if (row is null) return false;

        notes.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }
}
