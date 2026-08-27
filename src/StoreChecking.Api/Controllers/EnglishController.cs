using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreChecking.Application.English;

namespace StoreChecking.Api.Controllers;

/// <summary>Tiếng Anh — từ vựng đã lưu và câu giữ lại khi luyện nói.</summary>
[ApiController]
[Authorize]
[Route("api/english")]
[Tags("Tiếng Anh")]
[Produces("application/json")]
public sealed class EnglishController(EnglishService english) : ControllerBase
{
    // ---------- Saved vocabulary ----------

    /// <summary>Từ vựng đã lưu.</summary>
    /// <remarks>Mới nhất trước. limit mặc định 50, tối đa 200.</remarks>
    [HttpGet("words")]
    public async Task<IActionResult> ListWords(int? limit, int? offset, CancellationToken ct) =>
        Ok(await english.ListWordsAsync(limit, offset, ct));

    /// <summary>Lưu một từ kèm kết quả AI.</summary>
    [HttpPost("words")]
    public async Task<IActionResult> AddWord([FromBody] SaveEnglishWordRequest body, CancellationToken ct)
    {
        var created = await english.AddWordAsync(body, ct);
        return Created($"/api/english/words/{created.Id}", created);
    }

    /// <summary>Xoá một từ đã lưu.</summary>
    [HttpDelete("words/{id:guid}")]
    public async Task<IActionResult> DeleteWord(Guid id, CancellationToken ct) =>
        await english.DeleteWordAsync(id, ct) ? NoContent() : NotFound();

    // ---------- Sentences kept from speaking practice ----------

    /// <summary>Câu đã lưu khi luyện nói.</summary>
    /// <remarks>Mới nhất trước. `q` tìm trong cả nội dung câu lẫn ghi chú, không phân biệt hoa thường.</remarks>
    [HttpGet("sentences")]
    public async Task<IActionResult> ListSentences(int? limit, int? offset, string? q, CancellationToken ct) =>
        Ok(await english.ListSentencesAsync(limit, offset, q, ct));

    /// <summary>Lưu một câu.</summary>
    /// <remarks>Lưu trùng câu thì trả lại bản ghi cũ với mã 200, không tạo thêm dòng.</remarks>
    [HttpPost("sentences")]
    public async Task<IActionResult> AddSentence([FromBody] SaveSentenceRequest body, CancellationToken ct)
    {
        var (dto, created) = await english.AddSentenceAsync(body, ct);

        // 200 rather than 201 when the sentence was already there. The client shows a
        // bookmark toggle, so a double tap must read as "already saved", not as a new row.
        return created
            ? Created($"/api/english/sentences/{dto.Id}", dto)
            : Ok(dto);
    }

    /// <summary>Xoá một câu đã lưu.</summary>
    [HttpDelete("sentences/{id:guid}")]
    public async Task<IActionResult> DeleteSentence(Guid id, CancellationToken ct) =>
        await english.DeleteSentenceAsync(id, ct) ? NoContent() : NotFound();
}
