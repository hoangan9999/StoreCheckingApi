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
public sealed class MediaController(MediaService media, VideoJobQueue queue) : ControllerBase
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

    /// <summary>Dọn những dòng ảnh đã mất file trên đĩa.</summary>
    /// <remarks>
    /// Dòng mất file không xem được, không dựng video được, mà vẫn luôn được bộ chọn ưu
    /// tiên vì chưa dùng lần nào — để nguyên thì nó chặn việc dựng video vĩnh viễn.
    /// </remarks>
    [HttpPost("images/cleanup")]
    public async Task<IActionResult> Cleanup(CancellationToken ct) =>
        Ok(new { removed = await media.CleanupMissingAsync(ct) });

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

    /// <summary>Đánh dấu một video là đã tải về.</summary>
    /// <remarks>
    /// Giao diện gọi sau khi tải xong. Không tự đánh dấu khi ai đó mở file, vì nút "Xem"
    /// cũng đọc đúng file đó — xem thử không có nghĩa là đã lấy đi dùng.
    /// </remarks>
    [HttpPost("videos/{id:guid}/downloaded")]
    public async Task<IActionResult> MarkDownloaded(Guid id, CancellationToken ct) =>
        await media.MarkDownloadedAsync(id, ct) ? NoContent() : NotFound();

    [HttpDelete("videos/{id:guid}")]
    public async Task<IActionResult> DeleteVideo(Guid id, CancellationToken ct) =>
        await media.DeleteVideoAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Đặt một mẻ video để dựng ngay, không chờ tới lượt chạy hằng ngày.</summary>
    /// <remarks>
    /// Trả lời NGAY, việc nặng chạy ở tiến trình nền. Dựng năm video mất chừng năm phút và
    /// không request HTTP nào chờ nổi quãng đó. Theo dõi tiến độ bằng cách đọc lại danh sách
    /// video: cột `status` cho biết từng cái đang ở chặng nào.
    /// </remarks>
    [HttpPost("videos/generate")]
    public async Task<IActionResult> GenerateNow(int? count, CancellationToken ct)
    {
        var n = count is null or < 1
            ? Math.Max(await media.RemainingTodayAsync(ct), 1)
            : Math.Min(count.Value, MediaService.PerDay);

        if (!queue.Request(n))
            return Conflict(new { error = "Đang có mẻ video chạy dở, chờ xong rồi hãy bấm tiếp." });

        return Accepted(new { queued = n });
    }

    /// <summary>Hôm nay còn thiếu mấy video nữa cho đủ mẻ.</summary>
    [HttpGet("videos/remaining-today")]
    public async Task<IActionResult> RemainingToday(CancellationToken ct) =>
        Ok(new { remaining = await media.RemainingTodayAsync(ct), perDay = MediaService.PerDay });
}
