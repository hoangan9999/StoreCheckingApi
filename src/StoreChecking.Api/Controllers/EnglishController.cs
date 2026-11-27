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

    // ---------- Bài học theo chủ đề ----------

    /// <summary>Danh sách chủ đề, theo thứ tự người dùng tự sắp, kèm số dòng mỗi chủ đề.</summary>
    /// <remarks>Không trả nội dung bài — màn hình danh sách không hiện nội dung, và một chủ đề là hàng chục dòng.</remarks>
    /// <param name="kind">`lesson` (mặc định) hoặc `translate` — hai mục tách rời nhau.</param>
    /// <param name="ct">Token huỷ của request.</param>
    [HttpGet("topics")]
    public async Task<IActionResult> ListTopics(string? kind, CancellationToken ct) =>
        Ok(await english.ListTopicsAsync(kind, ct));

    /// <summary>Mở một chủ đề: tên và toàn bộ dòng trong đó, đúng thứ tự.</summary>
    [HttpGet("topics/{id:guid}")]
    public async Task<IActionResult> GetTopic(Guid id, CancellationToken ct) =>
        await english.GetTopicAsync(id, ct) is { } t ? Ok(t) : NotFound();

    /// <summary>Lưu các chủ đề dán từ nơi khác vào.</summary>
    /// <remarks>
    /// Nhận cả DANH SÁCH chủ đề và ghi trong một transaction: một lần dán thường mang
    /// nhiều chủ đề, mà vào được một nửa thì người dùng phải tự dò xem nửa nào đã vào.
    /// </remarks>
    [HttpPost("topics")]
    public async Task<IActionResult> ImportTopics([FromBody] ImportTopicsRequest body, CancellationToken ct) =>
        Ok(await english.ImportTopicsAsync(body, ct));

    /// <summary>Lưu lại cả một chủ đề: tên và mọi dòng trong đó.</summary>
    /// <remarks>
    /// Thay thế toàn phần. Sửa một chữ, xoá một dòng, thêm một dòng và đổi thứ tự đều là
    /// cùng một thao tác trên cùng một danh sách ngắn — tách thành bốn endpoint là bốn
    /// đường cho một bài lưu dở.
    /// </remarks>
    [HttpPut("topics/{id:guid}")]
    public async Task<IActionResult> SaveTopic(Guid id, [FromBody] SaveTopicRequest body, CancellationToken ct) =>
        await english.SaveTopicAsync(id, body, ct) is { } t ? Ok(t) : NotFound();

    /// <summary>Đánh dấu / bỏ dấu "đang học dang dở" cho một chủ đề.</summary>
    /// <remarks>
    /// Endpoint riêng chứ không phải một trường trong PUT ở trên: tích một ô không nên
    /// đòi phải cầm sẵn toàn bộ nội dung bài, và lưu bài sau khi sửa cũng không được phép
    /// vô tình xoá dấu vì thiếu trường đó.
    /// </remarks>
    [HttpPut("topics/{id:guid}/studying")]
    public async Task<IActionResult> SetStudying(
        Guid id, [FromBody] SetStudyingRequest body, CancellationToken ct) =>
        await english.SetStudyingAsync(id, body, ct) is { } t ? Ok(t) : NotFound();

    /// <summary>Xếp một chủ đề vào một loại, hoặc bỏ ra khỏi mọi loại (<c>null</c>).</summary>
    /// <remarks>
    /// Endpoint riêng, cùng lý do với <c>studying</c>: chọn loại không nên đòi cầm sẵn
    /// toàn bộ nội dung bài, và lưu bài sau khi sửa cũng không được phép bỏ phân loại chỉ
    /// vì thiếu trường đó. Không đụng vào <c>updatedAt</c> — xếp vào một mục thì không
    /// sửa gì trong bài cả.
    /// </remarks>
    [HttpPut("topics/{id:guid}/category")]
    public async Task<IActionResult> SetCategory(
        Guid id, [FromBody] SetTopicCategoryRequest body, CancellationToken ct) =>
        await english.SetCategoryAsync(id, body, ct) is { } t ? Ok(t) : NotFound();

    /// <summary>Xoá một chủ đề. Mọi dòng trong đó đi theo.</summary>
    [HttpDelete("topics/{id:guid}")]
    public async Task<IActionResult> DeleteTopic(Guid id, CancellationToken ct) =>
        await english.DeleteTopicAsync(id, ct) ? NoContent() : NotFound();

    // ---------- Loại chủ đề ----------

    /// <summary>Danh sách loại. Dùng chung cho cả mục Bài học lẫn mục Luyện dịch.</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> ListCategories(CancellationToken ct) =>
        Ok(await english.ListCategoriesAsync(ct));

    /// <summary>Thêm một loại. Trùng tên (bỏ qua hoa thường và khoảng trắng) thì từ chối.</summary>
    [HttpPost("categories")]
    public async Task<IActionResult> AddCategory(
        [FromBody] SaveCategoryRequest body, CancellationToken ct) =>
        Ok(await english.AddCategoryAsync(body, ct));

    /// <summary>Đổi tên một loại. Mọi chủ đề thuộc loại đó theo liền vì chúng giữ id.</summary>
    [HttpPut("categories/{id:guid}")]
    public async Task<IActionResult> RenameCategory(
        Guid id, [FromBody] SaveCategoryRequest body, CancellationToken ct) =>
        await english.RenameCategoryAsync(id, body, ct) is { } c ? Ok(c) : NotFound();

    /// <summary>Xoá một loại. Chủ đề thuộc loại đó Ở LẠI, chỉ rơi về "chưa phân loại".</summary>
    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct) =>
        await english.DeleteCategoryAsync(id, ct) ? NoContent() : NotFound();
}
