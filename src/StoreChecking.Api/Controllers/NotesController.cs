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
    /// <summary>Đính một ảnh vào ghi chú.</summary>
    [HttpPost("{id:guid}/images")]
    [RequestSizeLimit(30L * 1024 * 1024)]
    public async Task<IActionResult> AddImage(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "Chưa chọn ảnh." });
        if (!(file.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "File này không phải ảnh." });

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";

        await using var s = file.OpenReadStream();
        var updated = await notes.AddImageAsync(id, s, ext, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Xem một ảnh của ghi chú.</summary>
    [HttpGet("images/{filename}")]
    public IActionResult Image(string filename)
    {
        var s = notes.OpenImage(filename);
        return s is null ? NotFound() : File(s, "image/jpeg");
    }

    /// <summary>Gỡ một ảnh khỏi ghi chú.</summary>
    [HttpDelete("{id:guid}/images/{filename}")]
    public async Task<IActionResult> RemoveImage(Guid id, string filename, CancellationToken ct)
    {
        var updated = await notes.RemoveImageAsync(id, filename, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

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
