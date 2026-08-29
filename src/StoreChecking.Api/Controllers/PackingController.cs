using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreChecking.Application.Packing;

namespace StoreChecking.Api.Controllers;

/// <summary>Đóng gói — nhật ký video quay lúc đóng hàng.</summary>
/// <remarks>
/// Only the log lives here. The video files are on the NAS already and always were, reached
/// through nas.service.ts, so this module carries no file transfer at all — just the rows
/// that say which order was filmed and under what name.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/packing")]
[Tags("Đóng gói")]
[Produces("application/json")]
public sealed class PackingController(PackingService packing) : ControllerBase
{
    /// <summary>Nhật ký quay, mới nhất trước.</summary>
    /// <remarks>`search` lọc theo mã đơn, không phân biệt hoa thường. limit mặc định 100, tối đa 10000.</remarks>
    [HttpGet]
    public async Task<IActionResult> List(string? search, int? limit, CancellationToken ct) =>
        Ok(await packing.ListAsync(search, limit, ct));

    /// <summary>Ghi một lần quay, trả về số thứ tự và tên file để upload lên NAS.</summary>
    /// <remarks>
    /// Máy chủ tự đặt tên file (`&lt;mã đơn&gt;_&lt;seq&gt;.&lt;đuôi&gt;`) vì chỉ máy chủ biết
    /// mã đơn đó đã quay mấy lần. Client phải upload đúng tên đó lên NAS.
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SavePackingRequest body, CancellationToken ct)
    {
        var saved = await packing.SaveAsync(body, ct);
        return Created($"/api/packing?search={Uri.EscapeDataString(body.OrderCode ?? "")}", saved);
    }

    /// <summary>Xoá một dòng nhật ký.</summary>
    /// <remarks>Chỉ xoá dòng ghi, KHÔNG đụng tới file video trên NAS.</remarks>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await packing.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Tên mọi file đã ghi nhật ký — để biết trên NAS còn video nào chưa có dòng.</summary>
    [HttpGet("filenames")]
    public async Task<IActionResult> Filenames(CancellationToken ct) =>
        Ok(await packing.FilenamesAsync(ct));

    /// <summary>Nhập hàng loạt các video có trên NAS mà chưa có dòng nhật ký.</summary>
    /// <remarks>Tên file đã tồn tại thì bỏ qua, nên chạy lại sau khi nhập dở cũng không nhân đôi.</remarks>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ImportPackingRequest body, CancellationToken ct) =>
        Ok(await packing.ImportAsync(body, ct));
}
