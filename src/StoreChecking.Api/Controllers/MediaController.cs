using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreChecking.Application.Media;

namespace StoreChecking.Api.Controllers;

/// <summary>Kho ảnh và video tự sinh (tab Tiện ích).</summary>
[ApiController]
[Route("api/media")]
[Authorize]
[Tags("Kho ảnh & video")]
[Produces("application/json")]
public sealed class MediaController(MediaService media) : ControllerBase
{
    /// <summary>Kích thước tối đa một ảnh tải lên.</summary>
    private const long MaxImageBytes = 25L * 1024 * 1024;

    // ---------- Kho ảnh ----------

    /// <summary>Tải một hoặc nhiều ảnh vào kho.</summary>
    [HttpPost("images")]
    [RequestSizeLimit(300L * 1024 * 1024)]
    public async Task<IActionResult> Upload(List<IFormFile> files, CancellationToken ct)
    {
        if (files is null || files.Count == 0)
            return BadRequest(new { error = "Chưa chọn ảnh nào." });

        var saved = new List<MediaImageDto>();
        var skipped = new List<string>();

        foreach (var f in files)
        {
            // Bỏ qua từng file hỏng thay vì huỷ cả lượt: tải 50 ảnh mà một cái sai định dạng
            // thì mất công chọn lại từ đầu, trong khi 49 cái kia hoàn toàn dùng được.
            if (f.Length <= 0 || f.Length > MaxImageBytes) { skipped.Add(f.FileName); continue; }
            if (!(f.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(f.FileName);
                continue;
            }

            await using var s = f.OpenReadStream();
            saved.Add(await media.AddImageAsync(s, f.FileName, f.ContentType!, ct));
        }

        return Ok(new { saved = saved.Count, skipped, items = saved });
    }

    /// <summary>Một trang kho ảnh. `day` lọc đúng một ngày tải lên.</summary>
    [HttpGet("images")]
    public async Task<IActionResult> ListImages(DateOnly? day, int? limit, int? offset, CancellationToken ct) =>
        Ok(await media.ListImagesAsync(day, limit, offset, ct));

    /// <summary>Số ảnh theo từng ngày — mục lục của kho ảnh.</summary>
    [HttpGet("images/days")]
    public async Task<IActionResult> ImageDays(CancellationToken ct) => Ok(await media.ImageDaysAsync(ct));

    /// <summary>Xem một ảnh.</summary>
    [HttpGet("images/{filename}/file")]
    public IActionResult ImageFile(string filename)
    {
        var s = media.OpenImage(filename);
        return s is null ? NotFound() : File(s, "image/jpeg");
    }

    [HttpDelete("images/{id:guid}")]
    public async Task<IActionResult> DeleteImage(Guid id, CancellationToken ct) =>
        await media.DeleteImageAsync(id, ct) ? NoContent() : NotFound();

    // ---------- Kho video ----------

    /// <summary>Một trang kho video. `day` lọc theo ngày của mẻ.</summary>
    [HttpGet("videos")]
    public async Task<IActionResult> ListVideos(DateOnly? day, int? limit, int? offset, CancellationToken ct) =>
        Ok(await media.ListVideosAsync(day, limit, offset, ct));

    /// <summary>Số video theo từng ngày.</summary>
    [HttpGet("videos/days")]
    public async Task<IActionResult> VideoDays(CancellationToken ct) => Ok(await media.VideoDaysAsync(ct));

    /// <summary>Tải video về để đăng TikTok.</summary>
    [HttpGet("videos/{filename}/file")]
    public IActionResult VideoFile(string filename)
    {
        var s = media.OpenVideo(filename);
        // enableRangeProcessing: trình duyệt tua được video mà không phải tải lại từ đầu.
        return s is null ? NotFound() : File(s, "video/mp4", enableRangeProcessing: true);
    }

    [HttpDelete("videos/{id:guid}")]
    public async Task<IActionResult> DeleteVideo(Guid id, CancellationToken ct) =>
        await media.DeleteVideoAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Dựng ngay một video, không chờ tới lượt chạy hằng ngày.</summary>
    /// <remarks>Mất khoảng một phút: AI viết, giọng đọc, rồi ffmpeg ghép.</remarks>
    [HttpPost("videos/generate")]
    public async Task<IActionResult> GenerateNow(CancellationToken ct) =>
        Ok(await media.GenerateOneAsync(ct));

    /// <summary>Hôm nay còn thiếu mấy video nữa cho đủ mẻ.</summary>
    [HttpGet("videos/remaining-today")]
    public async Task<IActionResult> RemainingToday(CancellationToken ct) =>
        Ok(new { remaining = await media.RemainingTodayAsync(ct), perDay = MediaService.PerDay });
}
