using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Media;

public record MediaImageDto(
    Guid Id, string Filename, string OriginalName, long Bytes,
    int UseCount, DateTimeOffset UploadedAt);

public record GeneratedVideoDto(
    Guid Id, string? Filename, string Title, string Script, decimal? DurationSec,
    long? Bytes, string Status, string? Error, DateOnly BatchDay,
    DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, DateTimeOffset? DownloadedAt);

public record DayCountDto(DateOnly Day, int Count);

public record MediaPage<T>(int Total, int Limit, int Offset, IReadOnlyList<T> Items);

/// <summary>
/// The album, and the daily job that turns it into videos.
///
/// <para>Pictures in, five videos out, nobody watching. Each video is written by the AI from
/// the pictures themselves, read by the Adam voice, and assembled by ffmpeg.</para>
/// </summary>
public sealed class MediaService(
    IMediaImageRepository images,
    IGeneratedVideoRepository videos,
    IMediaStorage storage,
    IScriptWriter writer,
    IVoiceSynthesizer voice,
    IVideoRenderer renderer,
    IUnitOfWork uow,
    ICurrentUser user)
{
    /// <summary>Pictures per video. The ask was 10-15; the exact number is picked per video.</summary>
    private const int MinPerVideo = 10;
    private const int MaxPerVideo = 15;

    /// <summary>Videos a day.</summary>
    public const int PerDay = 5;

    private const int DefaultPage = 60;
    private const int MaxPage = 300;

    private static DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
            DateTimeOffset.UtcNow, "Asia/Ho_Chi_Minh").DateTime);

    // ---------- Kho ảnh ----------

    public async Task<MediaImageDto> AddImageAsync(
        Stream content, string originalName, string contentType, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalName);
        if (string.IsNullOrWhiteSpace(ext)) ext = contentType.Contains("png") ? ".png" : ".jpg";

        var filename = await storage.SaveImageAsync(content, ext, ct);

        var row = new MediaImage
        {
            UserId = user.Id,
            Filename = filename,
            OriginalName = (originalName ?? "").Trim(),
            ContentType = contentType,
            Bytes = 0,
        };

        // Size read back from disk rather than from the upload header: a client can claim
        // any length it likes, and what matters later is what actually landed.
        if (storage.ImagePath(filename) is { } path) row.Bytes = new FileInfo(path).Length;

        images.Add(row);
        await uow.SaveChangesAsync(ct);

        return ToDto(row);
    }

    public async Task<MediaPage<MediaImageDto>> ListImagesAsync(
        DateOnly? day, int? limit, int? offset, CancellationToken ct = default)
    {
        var take = limit is null or < 1 ? DefaultPage : Math.Min(limit.Value, MaxPage);
        var skip = Math.Max(offset ?? 0, 0);

        var (total, rows) = await images.ListAsync(day, skip, take, ct);
        return new MediaPage<MediaImageDto>(total, take, skip, rows.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<DayCountDto>> ImageDaysAsync(CancellationToken ct = default) =>
        (await images.CountByDayAsync(ct)).Select(r => new DayCountDto(r.Day, r.Count)).ToList();

    public async Task<bool> DeleteImageAsync(Guid id, CancellationToken ct = default)
    {
        var row = await images.FindAsync(id, ct);
        if (row is null) return false;

        images.Remove(row);
        await uow.SaveChangesAsync(ct);

        // File removed only after the row is gone. The other order can leave a row pointing
        // at nothing if the save fails, and a listing that shows broken pictures is worse
        // than a file nobody references.
        storage.DeleteImage(row.Filename);
        return true;
    }

    public Stream? OpenImage(string filename) => storage.OpenImage(filename);
    public Stream? OpenVideo(string filename) => storage.OpenVideo(filename);

    // ---------- Kho video ----------

    public async Task<MediaPage<GeneratedVideoDto>> ListVideosAsync(
        DateOnly? day, int? limit, int? offset, CancellationToken ct = default)
    {
        var take = limit is null or < 1 ? DefaultPage : Math.Min(limit.Value, MaxPage);
        var skip = Math.Max(offset ?? 0, 0);

        var (total, rows) = await videos.ListAsync(day, skip, take, ct);
        return new MediaPage<GeneratedVideoDto>(total, take, skip, rows.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<DayCountDto>> VideoDaysAsync(CancellationToken ct = default) =>
        (await videos.CountByDayAsync(ct)).Select(r => new DayCountDto(r.Day, r.Count)).ToList();

    public async Task<GeneratedVideoDto?> FindVideoAsync(Guid id, CancellationToken ct = default) =>
        await videos.FindAsync(id, ct) is { } row ? ToDto(row) : null;

    /// <summary>
    /// Đánh dấu đã tải. Gọi lại lần nữa không đổi gì — mốc đầu tiên mới là mốc thật.
    /// </summary>
    public async Task<bool> MarkDownloadedAsync(Guid id, CancellationToken ct = default)
    {
        var row = await videos.FindAsync(id, ct);
        if (row is null) return false;

        if (row.DownloadedAt is null)
        {
            row.DownloadedAt = DateTimeOffset.UtcNow;
            await uow.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task<bool> DeleteVideoAsync(Guid id, CancellationToken ct = default)
    {
        var row = await videos.FindAsync(id, ct);
        if (row is null) return false;

        videos.Remove(row);
        await uow.SaveChangesAsync(ct);

        if (row.Filename is { } f) storage.DeleteVideo(f);
        return true;
    }

    /// <summary>
    /// Xoá những dòng trỏ tới file không còn trên đĩa, trả về số dòng đã xoá.
    ///
    /// <para>Một dòng như thế không còn giá trị gì: không xem được, không dựng video được,
    /// mà lại luôn được bộ chọn ưu tiên vì chưa dùng lần nào. Để nguyên thì nó chặn tính
    /// năng vĩnh viễn. Chỉ xoá dòng — file vốn đã không còn để mà mất thêm.</para>
    /// </summary>
    public async Task<int> CleanupMissingAsync(CancellationToken ct = default)
    {
        var removed = 0;
        var offset = 0;

        while (true)
        {
            var (_, batch) = await images.ListAsync(null, offset, MaxPage, ct);
            if (batch.Count == 0) break;

            foreach (var row in batch)
            {
                if (storage.ImagePath(row.Filename) is null) { images.Remove(row); removed++; }
            }

            if (batch.Count < MaxPage) break;
            offset += MaxPage;
        }

        if (removed > 0) await uow.SaveChangesAsync(ct);
        return removed;
    }

    /// <summary>
    /// Xoá video dựng quá <paramref name="keepDays"/> ngày, kèm file của chúng.
    ///
    /// <para>Ngày nào cũng có năm video mới, nên video cũ không còn giá trị gì — và không ai
    /// dọn thì mỗi tháng thêm khoảng một GB nằm lại trên đĩa. Xoá bất kể đã tải hay chưa:
    /// một video năm ngày tuổi chưa đụng tới thì cũng sẽ không bao giờ đụng tới nữa.</para>
    ///
    /// <para>Quét cả file mồ côi — file có trên đĩa mà không dòng nào trỏ tới, xảy ra khi
    /// ffmpeg ghép xong nhưng lưu dòng thất bại. Chỉ đụng tới file đã quá hạn, nên một video
    /// đang ghép dở (tính bằng phút) không bao giờ nằm trong tầm ngắm.</para>
    /// </summary>
    public async Task<(int Rows, int Orphans)> CleanupOldVideosAsync(
        int keepDays, CancellationToken ct = default)
    {
        if (keepDays < 1) return (0, 0);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-keepDays);
        var old = await videos.ListOlderThanAsync(cutoff, ct);

        var files = old.Select(v => v.Filename).OfType<string>().ToList();

        if (old.Count > 0)
        {
            videos.RemoveRange(old);
            await uow.SaveChangesAsync(ct);

            // Xoá file SAU khi dòng đã đi. Ngược lại thì lưu thất bại sẽ để lại dòng trỏ vào
            // hư không, và một danh sách có video bấm vào không mở được thì khó chịu hơn là
            // một file thừa nằm im.
            foreach (var f in files) storage.DeleteVideo(f);
        }

        // File mồ côi: có trên đĩa, không dòng nào nhận.
        var known = (await videos.ListFilenamesAsync(ct)).ToHashSet(StringComparer.Ordinal);

        var orphans = 0;
        foreach (var name in storage.ListVideoFiles())
        {
            if (known.Contains(name)) continue;

            var path = storage.VideoPath(name);
            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) < cutoff.UtcDateTime)
            {
                storage.DeleteVideo(name);
                orphans++;
            }
        }

        return (old.Count, orphans);
    }

    // ---------- Dựng video ----------

    /// <summary>How many more are still owed today.</summary>
    public async Task<int> RemainingTodayAsync(CancellationToken ct = default) =>
        Math.Max(PerDay - await MadeTodayAsync(ct), 0);

    /// <summary>How many have been made today already, failures not counted.</summary>
    public Task<int> MadeTodayAsync(CancellationToken ct = default) =>
        videos.CountForDayAsync(Today(), ct);

    /// <summary>
    /// Builds one video, start to finish.
    ///
    /// <para>The row is written BEFORE the work starts and its status moves stage by stage.
    /// A crash then leaves a row saying exactly how far it got, instead of nothing at all —
    /// and "voicing" versus "rendering" are two very different things to go and fix.</para>
    /// </summary>
    public async Task<GeneratedVideoDto> GenerateOneAsync(CancellationToken ct = default)
    {
        var count = Random.Shared.Next(MinPerVideo, MaxPerVideo + 1);

        // Xin dư rồi lọc, thay vì xin đúng số cần.
        //
        // Một dòng có thể trỏ tới file không còn trên đĩa — đã xảy ra thật khi container được
        // dựng lại trước lúc volume tồn tại. Những dòng đó có use_count = 0 nên bộ chọn luôn
        // ưu tiên chúng, và nếu không lọc ra thì chúng sẽ làm hỏng MỌI lần dựng về sau chứ
        // không chỉ lần này. Lọc theo file thật sự có mặt là thứ duy nhất đáng tin.
        var candidates = await images.PickLeastUsedAsync(count * 3, ct);

        var usable = new List<(MediaImage Image, string Path)>();
        foreach (var c in candidates)
        {
            if (usable.Count >= count) break;
            if (storage.ImagePath(c.Filename) is { } p) usable.Add((c, p));
        }

        if (usable.Count < MinPerVideo)
        {
            var ghosts = candidates.Count - usable.Count;
            throw new InvalidOperationException(
                $"Chỉ có {usable.Count} ảnh dùng được, cần ít nhất {MinPerVideo}." +
                (ghosts > 0 ? $" ({ghosts} ảnh mất file trên đĩa — bấm \"Dọn ảnh hỏng\" rồi tải lại.)" : ""));
        }

        var picked = usable.Select(u => u.Image).ToList();
        var pickedPaths = usable.Select(u => u.Path).ToList();

        var row = new GeneratedVideo
        {
            UserId = user.Id,
            Status = VideoStatus.Writing,
            ImageIds = picked.Select(p => p.Id).ToArray(),
            BatchDay = Today(),
        };
        videos.Add(row);
        await uow.SaveChangesAsync(ct);

        var audio = Path.Combine(Path.GetTempPath(), $"voice-{row.Id:N}.mp3");

        try
        {
            var paths = pickedPaths;
            var script = await writer.WriteAsync(paths, ct);
            row.Title = script.Title;
            row.Script = script.Script;

            row.Status = VideoStatus.Voicing;
            await uow.SaveChangesAsync(ct);
            await voice.SpeakToFileAsync(script.Script, audio, ct);

            row.Status = VideoStatus.Rendering;
            await uow.SaveChangesAsync(ct);

            var name = $"video-{row.Id:N}.mp4";
            var outPath = storage.VideoPath(name);
            row.DurationSec = await renderer.RenderAsync(paths, audio, outPath, ct);

            row.Filename = name;
            row.Bytes = new FileInfo(outPath).Length;
            row.Status = VideoStatus.Ready;
            row.FinishedAt = DateTimeOffset.UtcNow;

            // Counted only once the video exists. Bumping it earlier would push pictures to
            // the back of the queue for a video that never got made.
            foreach (var p in picked)
            {
                p.UseCount++;
                p.LastUsedAt = DateTimeOffset.UtcNow;
            }

            await uow.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            row.Status = VideoStatus.Error;
            row.Error = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
            row.FinishedAt = DateTimeOffset.UtcNow;
            await uow.SaveChangesAsync(CancellationToken.None);

            // Ném tiếp để bên gọi biết; chặng hỏng đã ghi vào `status` và `error` của dòng,
            // còn chi tiết kỹ thuật thì Gemini/giọng đọc/ffmpeg đã tự ghi log ở tầng dưới.
            throw;
        }
        finally { try { File.Delete(audio); } catch { /* file tạm */ } }

        return ToDto(row);
    }

    private static MediaImageDto ToDto(MediaImage r) =>
        new(r.Id, r.Filename, r.OriginalName, r.Bytes, r.UseCount, r.UploadedAt);

    private static GeneratedVideoDto ToDto(GeneratedVideo r) =>
        new(r.Id, r.Filename, r.Title, r.Script, r.DurationSec, r.Bytes,
            r.Status, r.Error, r.BatchDay, r.CreatedAt, r.FinishedAt, r.DownloadedAt);
}
