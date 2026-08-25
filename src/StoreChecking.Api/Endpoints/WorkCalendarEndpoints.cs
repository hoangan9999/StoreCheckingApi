using Microsoft.EntityFrameworkCore;
using StoreChecking.Api.Auth;
using StoreChecking.Api.Data;
using StoreChecking.Api.Dtos;
using StoreChecking.Api.Models;

namespace StoreChecking.Api.Endpoints;

public static class WorkCalendarEndpoints
{
    /// <summary>Date format shared with the Angular client: 'YYYY-MM-DD'.</summary>
    private const string DayFormat = "yyyy-MM-dd";

    private static string S(DateOnly d) => d.ToString(DayFormat);

    private static bool TryDay(string? raw, out DateOnly day) =>
        DateOnly.TryParseExact(raw, DayFormat, out day);

    public static RouteGroupBuilder MapWorkCalendar(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/work-calendar").RequireAuthorization().WithTags("Lịch làm");

        // ---------- Day cells ----------

        // GET /api/work-calendar/days?from=2026-09-26&to=2026-10-25
        g.MapGet("/days", async (string? from, string? to, AppDbContext db) =>
        {
            if (!TryDay(from, out var f) || !TryDay(to, out var t))
                return Results.BadRequest(new { error = "Cần from và to dạng YYYY-MM-DD." });
            if (t < f)
                return Results.BadRequest(new { error = "to phải >= from." });

            var rows = await db.WorkDays
                .Where(x => x.Day >= f && x.Day <= t)
                .OrderBy(x => x.Day)
                .Select(x => new WorkDayDto(x.Id, x.Day.ToString(DayFormat), x.Note, x.Color))
                .ToListAsync();

            return Results.Ok(rows);
        })
        .WithSummary("Ô ngày trong khoảng")
        .WithDescription("from/to dạng YYYY-MM-DD. Chu kỳ lịch chạy 26 tháng trước → 25 tháng này.");

        // PUT /api/work-calendar/days/2026-10-01
        // A cell with no note and no colour is DELETED rather than stored, so the table
        // does not fill up with empty rows. Matches the previous Supabase behaviour.
        g.MapPut("/days/{day}", async (string day, SaveWorkDayRequest body,
                                       AppDbContext db, CurrentUser me) =>
        {
            if (!TryDay(day, out var d))
                return Results.BadRequest(new { error = "Ngày phải dạng YYYY-MM-DD." });

            var note = (body.Note ?? "").Trim();
            var color = string.IsNullOrWhiteSpace(body.Color) ? null : body.Color.Trim();
            var row = await db.WorkDays.FirstOrDefaultAsync(x => x.Day == d);

            if (note.Length == 0 && color is null)
            {
                if (row is not null) db.WorkDays.Remove(row);
                await db.SaveChangesAsync();
                return Results.NoContent();
            }

            if (row is null)
            {
                row = new WorkDay { UserId = me.Id, Day = d };
                db.WorkDays.Add(row);
            }
            row.Note = body.Note ?? "";
            row.Color = color;
            row.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(new WorkDayDto(row.Id, S(row.Day), row.Note, row.Color));
        })
        .WithSummary("Ghi một ô ngày")
        .WithDescription("Không ghi chú và không màu thì XOÁ hẳn dòng, trả 204.");

        // ---------- Month notes ----------

        // GET /api/work-calendar/notes?period=2026-10-01
        g.MapGet("/notes", async (string? period, AppDbContext db) =>
        {
            if (!TryDay(period, out var p))
                return Results.BadRequest(new { error = "Cần period dạng YYYY-MM-01." });

            var rows = await db.WorkMonthNotes
                .Where(x => x.Period == p)
                .OrderBy(x => x.Sort).ThenBy(x => x.CreatedAt)
                .Select(x => new MonthNoteDto(x.Id, x.Period.ToString(DayFormat), x.Content, x.Sort))
                .ToListAsync();

            return Results.Ok(rows);
        })
        .WithSummary("Ghi chú chung của tháng")
        .WithDescription("period = ngày 1 của tháng, ví dụ 2026-10-01.");

        // POST /api/work-calendar/notes
        g.MapPost("/notes", async (CreateMonthNoteRequest body, AppDbContext db, CurrentUser me) =>
        {
            if (!TryDay(body.Period, out var p))
                return Results.BadRequest(new { error = "Cần period dạng YYYY-MM-01." });

            var row = new WorkMonthNote
            {
                UserId = me.Id,
                Period = p,
                Content = "",
                Sort = body.Sort,
            };
            db.WorkMonthNotes.Add(row);
            await db.SaveChangesAsync();

            return Results.Created($"/api/work-calendar/notes/{row.Id}",
                new MonthNoteDto(row.Id, S(row.Period), row.Content, row.Sort));
        })
        .WithSummary("Thêm một dòng ghi chú trống");

        // PUT /api/work-calendar/notes/{id}
        g.MapPut("/notes/{id:guid}", async (Guid id, UpdateMonthNoteRequest body, AppDbContext db) =>
        {
            var row = await db.WorkMonthNotes.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null) return Results.NotFound();

            row.Content = body.Content ?? "";
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new MonthNoteDto(row.Id, S(row.Period), row.Content, row.Sort));
        })
        .WithSummary("Sửa nội dung một dòng");

        // DELETE /api/work-calendar/notes/{id}
        g.MapDelete("/notes/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var row = await db.WorkMonthNotes.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null) return Results.NotFound();

            db.WorkMonthNotes.Remove(row);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithSummary("Xoá một dòng ghi chú");

        return g;
    }
}
