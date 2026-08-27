using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreChecking.Application.Notes;

namespace StoreChecking.Api.Controllers;

/// <summary>Ghi chú nhanh — thứ hay phải copy lại: STK, mẫu tin nhắn, bảng size.</summary>
/// <remarks>
/// Not to be confused with /api/work-calendar/notes, which are the month-note lines under
/// the work calendar. Different table, different feature, similar name.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/notes")]
[Tags("Ghi chú")]
[Produces("application/json")]
public sealed class NotesController(NotesService notes) : ControllerBase
{
    /// <summary>Tất cả ghi chú, mới sửa gần nhất lên đầu.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await notes.ListAsync(ct));

    /// <summary>Thêm một ghi chú.</summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] SaveNoteRequest body, CancellationToken ct)
    {
        var created = await notes.AddAsync(body, ct);
        return Created($"/api/notes/{created.Id}", created);
    }

    /// <summary>Sửa tiêu đề và nội dung một ghi chú.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveNoteRequest body, CancellationToken ct)
    {
        var updated = await notes.UpdateAsync(id, body, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Xoá một ghi chú.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await notes.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
