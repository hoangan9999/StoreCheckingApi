using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreChecking.Api;
using StoreChecking.Application.Abstractions;

namespace StoreChecking.Api.Controllers;

/// <summary>Hệ thống — kiểm tra sống chết và kiểm tra token.</summary>
[ApiController]
[Tags("Hệ thống")]
[Produces("application/json")]
public sealed class SystemController(IDatabaseHealth health, ICurrentUser me) : ControllerBase
{
    /// <summary>Sống chưa, DB nối được chưa (không cần token).</summary>
    /// <remarks>
    /// Trả kèm `dbMs` và `idleSec` để chẩn đoán những lần chậm sau khi app nghỉ lâu.
    /// `dbMs` gần bằng tổng thời gian phản hồi thì nút thắt ở database hoặc ổ đĩa;
    /// `dbMs` nhỏ mà vẫn chậm thì nút thắt ở tiến trình hoặc ở chính NAS.
    /// </remarks>
    [HttpGet("/health")]
    [AllowAnonymous]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var dbOk = await health.CanConnectAsync(ct);
        var dbMs = clock.ElapsedMilliseconds;

        var idle = HttpContext.Items[LastRequestClock.ItemKey] as TimeSpan? ?? TimeSpan.Zero;

        // `version` is baked into the image at build time (see Dockerfile) and is what
        // makes a deploy verifiable from outside: tools/deploy.ps1 waits until this
        // reports the exact commit it pushed. Drop the field and every deploy silently
        // times out instead of reporting success.
        var version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";

        return Ok(new { ok = true, db = dbOk, version, dbMs, idleSec = (int)idle.TotalSeconds });
    }

    /// <summary>Token có hợp lệ không, user id là ai.</summary>
    [HttpGet("/api/me")]
    [Authorize]
    public IActionResult Me() => Ok(new
    {
        // Through ICurrentUser like everything else, so there is exactly one place that
        // decides where a user id comes from.
        userId = me.Id,
        email = User.FindFirst("email")?.Value,
    });
}
