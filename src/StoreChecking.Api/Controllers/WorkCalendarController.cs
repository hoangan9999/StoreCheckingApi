using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreChecking.Application.WorkCalendar;

namespace StoreChecking.Api.Controllers;

/// <summary>Lịch làm — ô ngày và ghi chú chung của tháng.</summary>
[ApiController]
[Authorize]
[Route("api/work-calendar")]
[Tags("Lịch làm")]
[Produces("application/json")]
public sealed class WorkCalendarController(WorkCalendarService calendar) : ControllerBase
{
    // Date parsing lives here rather than in the service: a badly shaped request is an
    // HTTP concern, and the service should never have to take a string it might reject.
    // The messages are Vietnamese because they reach the user, and they are part of the
    // contract the tests pin down.
    private static bool TryDay(string? raw, out DateOnly day) =>
        DateOnly.TryParseExact(raw, WorkCalendarService.DayFormat, out day);

    // ---------- Day cells ----------

    /// <summary>Ô ngày trong khoảng.</summary>
    /// <remarks>from/to dạng YYYY-MM-DD. Chu kỳ lịch chạy 26 tháng trước → 25 tháng này.</remarks>
    [HttpGet("days")]
    public async Task<IActionResult> ListDays(string? from, string? to, CancellationToken ct)
    {
        if (!TryDay(from, out var f) || !TryDay(to, out var t))
            return BadRequest(new { error = "Cần from và to dạng YYYY-MM-DD." });
        if (t < f)
            return BadRequest(new { error = "to phải >= from." });

        return Ok(await calendar.ListDaysAsync(f, t, ct));
    }

    /// <summary>Ghi một ô ngày.</summary>
    /// <remarks>Không ghi chú và không màu thì XOÁ hẳn dòng, trả 204.</remarks>
    [HttpPut("days/{day}")]
    public async Task<IActionResult> SaveDay(string day, [FromBody] SaveWorkDayRequest body, CancellationToken ct)
    {
        if (!TryDay(day, out var d))
            return BadRequest(new { error = "Ngày phải dạng YYYY-MM-DD." });

        var saved = await calendar.SaveDayAsync(d, body, ct);
        return saved is null ? NoContent() : Ok(saved);
    }

    // ---------- Month notes ----------

    /// <summary>Ghi chú chung của tháng.</summary>
    /// <remarks>period = ngày 1 của tháng, ví dụ 2026-10-01.</remarks>
    [HttpGet("notes")]
    public async Task<IActionResult> ListNotes(string? period, CancellationToken ct)
    {
        if (!TryDay(period, out var p))
            return BadRequest(new { error = "Cần period dạng YYYY-MM-01." });

        return Ok(await calendar.ListNotesAsync(p, ct));
    }

    /// <summary>Thêm một dòng ghi chú trống.</summary>
    [HttpPost("notes")]
    public async Task<IActionResult> AddNote([FromBody] CreateMonthNoteRequest body, CancellationToken ct)
    {
        if (!TryDay(body.Period, out var p))
            return BadRequest(new { error = "Cần period dạng YYYY-MM-01." });

        var created = await calendar.AddNoteAsync(p, body.Sort, ct);
        return Created($"/api/work-calendar/notes/{created.Id}", created);
    }

    /// <summary>Sửa nội dung một dòng.</summary>
    [HttpPut("notes/{id:guid}")]
    public async Task<IActionResult> UpdateNote(Guid id, [FromBody] UpdateMonthNoteRequest body, CancellationToken ct)
    {
        var updated = await calendar.UpdateNoteAsync(id, body.Content, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Xoá một dòng ghi chú.</summary>
    [HttpDelete("notes/{id:guid}")]
    public async Task<IActionResult> DeleteNote(Guid id, CancellationToken ct) =>
        await calendar.DeleteNoteAsync(id, ct) ? NoContent() : NotFound();
}
