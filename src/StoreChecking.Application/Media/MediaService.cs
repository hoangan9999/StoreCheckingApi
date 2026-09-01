using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Media;

public record MediaImageDto(
    Guid Id, string Filename, string OriginalName, long Bytes,
    int UseCount, DateTimeOffset UploadedAt);

public record GeneratedVideoDto(
    Guid Id, string? Filename, string Title, string Script, decimal? DurationSec,
    long? Bytes, string Status, string? Error, DateOnly BatchDay,
    DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, DateTimeOffset? DownloadedAt,
    DateTimeOffset? PostedAt, string? FbPostId, string? PostError);

public record DayCountDto(DateOnly Day, int Count);

/// <param name="AutoPost">Dựng xong thì đăng luôn lên Fanpage.</param>
/// <param name="FanpageReady">
/// Máy chủ có khoá Facebook hay không. Giao diện cần biết để nói rõ vì sao bật mà không
/// đăng được, thay vì để người dùng bật rồi ngồi chờ một bài không bao giờ lên.
/// </param>
public record VideoSettingsDto(
    bool AutoPost, bool FanpageReady, bool MakeVideos, bool MakePosts);

public record GeneratedPostDto(
    Guid Id, Guid ImageId, string ImageFilename, string Title, string Content,
    string Status, string? Error, DateOnly BatchDay, DateTimeOffset CreatedAt,
    DateTimeOffset? PostedAt, string? FbPostId, string? PostError);

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
    IFanpagePublisher fanpage,
    IAppSettingRepository settings,
    IGeneratedPostRepository posts,
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

            // Đăng lên Fanpage sau khi đã lưu trạng thái `ready`.
            //
            // Nằm TRONG try nhưng tự nuốt lỗi của chính nó: video đã dựng xong rồi, đăng
            // hỏng không được phép biến nó thành video hỏng — file vẫn tải về đăng tay được.
            if (fanpage.Configured && await AutoPostAsync(ct)) await TryPostAsync(row, ct);
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

    // ---------- Cài đặt ----------

    /// <summary>
    /// Có tự đăng lên Fanpage sau khi dựng xong không. Mặc định BẬT.
    /// </summary>
    /// <remarks>
    /// Đọc từ database chứ không phải cấu hình: đổi bằng một cái checkbox trong app, không
    /// phải sửa .env rồi dựng lại container.
    /// </remarks>
    private async Task<bool> AutoPostAsync(CancellationToken ct)
    {
        var row = await settings.FindAsync(SettingKeys.VideoAutoPost, ct);
        return row is null || row.Value == "true";
    }

    /// <summary>Một công tắc bật/tắt. Chưa có dòng nào thì coi như BẬT.</summary>
    private async Task<bool> OnAsync(string key, CancellationToken ct)
    {
        var row = await settings.FindAsync(key, ct);
        return row is null || row.Value == "true";
    }

    public async Task<VideoSettingsDto> GetSettingsAsync(CancellationToken ct = default) =>
        new(await AutoPostAsync(ct), fanpage.Configured,
            await OnAsync(SettingKeys.MakeVideos, ct),
            await OnAsync(SettingKeys.MakePosts, ct));

    /// <summary>Đặt một công tắc rồi trả về toàn bộ cài đặt như nó vừa thành.</summary>
    public async Task<VideoSettingsDto> SetSwitchAsync(
        string key, bool on, CancellationToken ct = default)
    {
        if (key is not (SettingKeys.VideoAutoPost or SettingKeys.MakeVideos or SettingKeys.MakePosts))
            throw new InvalidOperationException($"Không có cài đặt tên \"{key}\".");

        var row = await settings.FindAsync(key, ct);

        if (row is null)
        {
            row = new AppSetting { UserId = user.Id, Key = key };
            settings.Add(row);
        }

        row.Value = on ? "true" : "false";
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);

        return await GetSettingsAsync(ct);
    }

    /// <summary>
    /// Đăng một video lên Fanpage. Trả về false nếu không đăng được.
    /// </summary>
    /// <remarks>
    /// Không bao giờ ném ra ngoài. Đăng bài là việc phụ sau khi video đã xong; hỏng thì ghi
    /// lý do vào `post_error` để còn thấy mà thử lại, chứ không kéo theo cả lần dựng.
    /// </remarks>
    private async Task<bool> TryPostAsync(GeneratedVideo row, CancellationToken ct)
    {
        if (row.Filename is null) return false;

        try
        {
            var post = await fanpage.PostVideoAsync(
                storage.VideoPath(row.Filename), row.Title, fanpage.BuildCaption(row.Script), ct);

            row.PostedAt = DateTimeOffset.UtcNow;
            row.FbPostId = post.PostId;
            row.PostError = null;
        }
        catch (Exception ex)
        {
            row.PostError = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
        }

        // CancellationToken.None: nếu bị huỷ giữa chừng thì vẫn phải ghi lại kết quả, không
        // thì bài đã lên Facebook mà ở đây không biết, và lần sau sẽ đăng lại lần nữa.
        await uow.SaveChangesAsync(CancellationToken.None);
        return row.PostedAt is not null;
    }

    /// <summary>
    /// Đăng tay một video lên Fanpage — cho video tự đăng hỏng, hoặc lúc tắt tự đăng.
    /// </summary>
    public async Task<GeneratedVideoDto?> PostToFanpageAsync(Guid id, CancellationToken ct = default)
    {
        var row = await videos.FindAsync(id, ct);
        if (row is null) return null;

        if (row.Status != VideoStatus.Ready || row.Filename is null)
            throw new InvalidOperationException("Video chưa dựng xong, chưa đăng được.");

        // Đăng hai lần thì Fanpage có hai bài y hệt nhau, mà gỡ thì phải vào tận Facebook.
        if (row.PostedAt is not null)
            throw new InvalidOperationException("Video này đã đăng lên Fanpage rồi.");

        if (!fanpage.Configured)
            throw new InvalidOperationException(
                "Chưa khai FB_PAGE_ID / FB_PAGE_ACCESS_TOKEN cho máy chủ.");

        await TryPostAsync(row, ct);

        // Lỗi nằm trong `post_error` của dòng trả về, nên bên gọi vẫn thấy vì sao hỏng.
        return ToDto(row);
    }

    // ---------- Bài đăng Fanpage ----------

    /// <summary>Bài mỗi ngày. Cùng số với video, và đăng theo cùng những khung giờ đó.</summary>
    public const int PostsPerDay = 5;

    /// <summary>
    /// Viết cả mẻ bài của hôm nay trong MỘT lượt gọi Gemini.
    /// </summary>
    /// <remarks>
    /// Mỗi bài một ảnh, và cả năm ảnh đi trong cùng một lượt. Hạn mức Gemini bị chặn ở SỐ
    /// LƯỢT GỌI (20/ngày) chứ không phải dung lượng, nên gọi riêng từng bài sẽ lấy mất 5
    /// lượt của tab tiếng Anh để làm đúng việc mà một lượt làm xong.
    /// <para>Không làm gì nếu hôm nay đã có mẻ — gọi lại được, không sinh bài trùng.</para>
    /// </remarks>
    public async Task<int> WriteTodaysPostsAsync(CancellationToken ct = default)
    {
        var day = Today();
        if (await posts.CountForDayAsync(day, ct) > 0) return 0;

        // Xin dư rồi lọc theo file có thật, y như bên video: một dòng có thể trỏ tới file đã
        // biến mất, và những dòng đó có use_count = 0 nên luôn được bộ chọn ưu tiên.
        var candidates = await images.PickLeastUsedAsync(PostsPerDay * 3, ct);

        var usable = new List<(MediaImage Image, string Path)>();
        foreach (var c in candidates)
        {
            if (usable.Count >= PostsPerDay) break;
            if (storage.ImagePath(c.Filename) is { } p) usable.Add((c, p));
        }

        if (usable.Count == 0)
            throw new InvalidOperationException("Kho ảnh chưa có ảnh nào dùng được để viết bài.");

        var written = await writer.WritePostsAsync(usable.Select(u => u.Path).ToList(), ct);

        // Ghép theo VỊ TRÍ. Gemini có thể trả về ít hơn số ảnh đã gửi, nên lấy phần chung —
        // thừa lại vài tấm ảnh thì chúng chỉ đơn giản là chưa được dùng lần này.
        var n = Math.Min(written.Count, usable.Count);

        for (var i = 0; i < n; i++)
        {
            posts.Add(new GeneratedPost
            {
                UserId = user.Id,
                ImageId = usable[i].Image.Id,
                Title = written[i].Title,
                Content = written[i].Content,
                Status = PostStatus.Ready,
                BatchDay = day,
                // Cách nhau một tích để thứ tự đăng trong ngày là cố định, không phụ thuộc
                // vào việc database sắp xếp các dòng cùng thời điểm thế nào.
                CreatedAt = DateTimeOffset.UtcNow.AddTicks(i),
            });

            usable[i].Image.UseCount++;
            usable[i].Image.LastUsedAt = DateTimeOffset.UtcNow;
        }

        await uow.SaveChangesAsync(ct);
        return n;
    }

    /// <summary>
    /// Đăng bài tiếp theo trong ngày lên Fanpage. Trả về false khi không còn gì để đăng.
    /// </summary>
    public async Task<bool> PostNextAsync(CancellationToken ct = default)
    {
        var row = await posts.NextUnpostedAsync(Today(), ct);
        if (row is null) return false;

        return await TryPostPhotoAsync(row, ct);
    }

    /// <summary>Đăng tay một bài cụ thể.</summary>
    public async Task<GeneratedPostDto?> PostArticleToFanpageAsync(Guid id, CancellationToken ct = default)
    {
        var row = await posts.FindAsync(id, ct);
        if (row is null) return null;

        if (row.Status != PostStatus.Ready)
            throw new InvalidOperationException("Bài chưa viết xong, chưa đăng được.");

        // Đăng hai lần thì Fanpage có hai bài y hệt, mà gỡ thì phải vào tận Facebook.
        if (row.PostedAt is not null)
            throw new InvalidOperationException("Bài này đã đăng lên Fanpage rồi.");

        if (!fanpage.Configured)
            throw new InvalidOperationException("Chưa khai FB_PAGE_ID / FB_PAGE_ACCESS_TOKEN cho máy chủ.");

        await TryPostPhotoAsync(row, ct);
        return await ToDtoAsync(row, ct);
    }

    /// <summary>Đăng một bài. Không bao giờ ném — lý do hỏng ghi vào `post_error`.</summary>
    private async Task<bool> TryPostPhotoAsync(GeneratedPost row, CancellationToken ct)
    {
        var image = await images.FindAsync(row.ImageId, ct);
        var path = image is null ? null : storage.ImagePath(image.Filename);

        if (path is null)
        {
            row.PostError = "Ảnh của bài này không còn trên đĩa.";
            await uow.SaveChangesAsync(CancellationToken.None);
            return false;
        }

        try
        {
            var post = await fanpage.PostPhotoAsync(path, fanpage.BuildCaption(row.Content), ct);
            row.PostedAt = DateTimeOffset.UtcNow;
            row.FbPostId = post.PostId;
            row.PostError = null;
        }
        catch (Exception ex)
        {
            row.PostError = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
        }

        // CancellationToken.None: bị huỷ giữa chừng thì vẫn phải ghi kết quả, không thì bài
        // đã lên Facebook mà ở đây không biết, và lần sau sẽ đăng lại lần nữa.
        await uow.SaveChangesAsync(CancellationToken.None);
        return row.PostedAt is not null;
    }

    public async Task<MediaPage<GeneratedPostDto>> ListPostsAsync(
        DateOnly? day, int? limit, int? offset, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? DefaultPage, 1, MaxPage);
        var skip = Math.Max(offset ?? 0, 0);

        var (total, rows) = await posts.ListAsync(day, skip, take, ct);

        var items = new List<GeneratedPostDto>(rows.Count);
        foreach (var r in rows) items.Add(await ToDtoAsync(r, ct));

        return new MediaPage<GeneratedPostDto>(total, take, skip, items);
    }

    public async Task<int> PostsMadeTodayAsync(CancellationToken ct = default) =>
        await posts.CountPostedForDayAsync(Today(), ct);

    /// <summary>Có tự viết bài mỗi ngày không.</summary>
    public Task<bool> MakePostsEnabledAsync(CancellationToken ct = default) =>
        OnAsync(SettingKeys.MakePosts, ct);

    /// <summary>Có tự dựng video mỗi ngày không.</summary>
    public Task<bool> MakeVideosEnabledAsync(CancellationToken ct = default) =>
        OnAsync(SettingKeys.MakeVideos, ct);

    public Task<bool> AutoPostEnabledAsync(CancellationToken ct = default) => AutoPostAsync(ct);

    /// <summary>Dọn bài cũ, cùng mốc ngày với video.</summary>
    public async Task<int> CleanupOldPostsAsync(int keepDays, CancellationToken ct = default)
    {
        var cutoff = Today().AddDays(-Math.Max(keepDays, 1));
        var old = await posts.ListOlderThanAsync(cutoff, ct);
        if (old.Count == 0) return 0;

        posts.RemoveRange(old);
        await uow.SaveChangesAsync(ct);
        return old.Count;
    }

    /// <summary>Tên file ảnh đi kèm, để giao diện hiện được ảnh của bài.</summary>
    private async Task<GeneratedPostDto> ToDtoAsync(GeneratedPost r, CancellationToken ct)
    {
        var image = await images.FindAsync(r.ImageId, ct);
        return new GeneratedPostDto(
            r.Id, r.ImageId, image?.Filename ?? "", r.Title, r.Content,
            r.Status, r.Error, r.BatchDay, r.CreatedAt, r.PostedAt, r.FbPostId, r.PostError);
    }

    private static MediaImageDto ToDto(MediaImage r) =>
        new(r.Id, r.Filename, r.OriginalName, r.Bytes, r.UseCount, r.UploadedAt);

    private static GeneratedVideoDto ToDto(GeneratedVideo r) =>
        new(r.Id, r.Filename, r.Title, r.Script, r.DurationSec, r.Bytes,
            r.Status, r.Error, r.BatchDay, r.CreatedAt, r.FinishedAt, r.DownloadedAt,
            r.PostedAt, r.FbPostId, r.PostError);
}
