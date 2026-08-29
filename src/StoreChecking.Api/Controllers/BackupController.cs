using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreChecking.Application.Backup;

namespace StoreChecking.Api.Controllers;

/// <summary>Sao lưu — đọc toàn bộ dữ liệu để ghi ra một file JSON trên NAS.</summary>
[ApiController]
[Authorize]
[Route("api/backup")]
[Tags("Sao lưu")]
[Produces("application/json")]
public sealed class BackupController(BackupService backup) : ControllerBase
{
    /// <summary>Toàn bộ dữ liệu của người đang đăng nhập, theo bảng.</summary>
    /// <remarks>
    /// Trả về `{ counts, tables }` — đúng hình dạng các file sao lưu cũ đang có, với tên cột
    /// nguyên gốc của database. Đổi hình dạng ở đây là làm hỏng khả năng đọc lại file cũ.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> Dump(CancellationToken ct) => Ok(await backup.DumpAsync(ct));
}
