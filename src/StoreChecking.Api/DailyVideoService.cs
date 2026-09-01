using StoreChecking.Api.Auth;
using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Media;

namespace StoreChecking.Api;

/// <summary>
/// Builds the day's videos on its own, so there is a fresh batch waiting each morning.
///
/// <para>Modelled on the database warm-up service beside it: wake on a timer, check whether
/// there is work, do it, go back to sleep. No cron, no scheduler to install.</para>
///
/// <para>Catching up matters more than being punctual. If the machine was off at the hour
/// this was meant to run, the count for the day is still short when it comes back on, so
/// the batch simply gets made then. A fixed alarm would have missed the day entirely.</para>
/// </summary>
public sealed class DailyVideoService(
    IServiceScopeFactory scopes,
    VideoJobQueue queue,
    ILogger<DailyVideoService> log,
    int[] slots,
    int keepVideoDays,
    bool enabled) : BackgroundService
{
    /// <summary>How often to look. Cheap: one count query when there is nothing to do.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Breathing room between videos.
    /// <para>Five in a row is five Gemini calls, five voice renders and five ffmpeg passes.
    /// Spacing them keeps the machine usable while it happens, and keeps a burst of image
    /// uploads from hitting the AI quota all at once.</para>
    /// </summary>
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!enabled)
        {
            log.LogInformation("Không tự dựng video hằng ngày (đã tắt bằng cấu hình).");
            return;
        }

        log.LogInformation("Tự dựng video vào các khung giờ: {Slots}.", string.Join("h, ", slots) + "h");

        // A first look after a short delay rather than immediately: startup is already busy
        // with the schema check and the warm-up, and a video render would pile on top.
        try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Chờ tối đa Interval, nhưng tỉnh NGAY khi có người bấm "Dựng ngay". Một cái
                // hẹn giờ đơn thuần sẽ bắt người bấm nút chờ tới mười lăm phút mới thấy động
                // tĩnh gì.
                var requested = await WaitForWorkAsync(ct);

                if (requested is { } n) await GenerateAsync(n, "theo yêu cầu", ct);
                else await RunDailyAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Never let the loop die. A batch that failed today should be retried, not
                // silently stop the feature until somebody restarts the container.
                log.LogWarning(ex, "Lượt dựng video hỏng, sẽ thử lại lượt sau.");
            }
        }
    }

    /// <summary>Trả về số video được yêu cầu, hoặc null khi chỉ là hết giờ chờ định kỳ.</summary>
    private async Task<int?> WaitForWorkAsync(CancellationToken ct)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(Interval);

        try { return await queue.Reader.ReadAsync(wait.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;                    // hết giờ chờ, tới lượt kiểm định kỳ
        }
    }

    /// <summary>
    /// Lượt kiểm định kỳ: tới giờ nào rồi thì phải có đủ bấy nhiêu video.
    ///
    /// <para>Đếm số khung giờ ĐÃ QUA trong ngày rồi so với số video đã có, thay vì hẹn đúng
    /// từng mốc. Cách này tự bù: máy tắt cả buổi sáng, bật lên lúc 15h thì ba khung 7h, 11h,
    /// 14h đều đã qua nên nó dựng bù ba video. Hẹn đúng mốc thì ba khung đó mất trắng.</para>
    ///
    /// <para>Cũng nhờ vậy mà lượt kiểm chạy mỗi mười lăm phút là đủ — không cần canh đúng
    /// phút, chỉ cần đúng số.</para>
    /// </summary>
    private async Task RunDailyAsync(CancellationToken ct)
    {
        await CleanupAsync(ct);

        var vnNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Ho_Chi_Minh");
        var due = slots.Count(h => vnNow.Hour >= h);
        if (due == 0) return;

        // Bài viết trước, video sau. Bài rẻ hơn nhiều — một lượt Gemini cho cả mẻ, không có
        // giọng đọc, không ffmpeg — nên nếu máy sắp tắt thì ít ra bài đã kịp lên.
        await RunPostsAsync(due, ct);
        await GenerateAsync(null, $"theo lịch — đã qua {due} khung giờ", ct, due);
    }

    /// <summary>
    /// Dọn video quá hạn. Chạy cùng lượt kiểm định kỳ vì nó rẻ khi không có gì để dọn:
    /// một câu truy vấn theo mốc thời gian, thường trả về rỗng.
    /// </summary>
    private async Task CleanupAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var owner = await sp.GetRequiredService<IMediaImageRepository>().FindOwnerAsync(ct);
        if (owner is null) return;

        sp.GetRequiredService<ScopeUser>().RunAs(owner.Value);

        var (rows, orphans) = await sp.GetRequiredService<MediaService>()
            .CleanupOldVideosAsync(keepVideoDays, ct);

        if (rows > 0 || orphans > 0)
            log.LogInformation("Đã dọn {Rows} video quá {Days} ngày và {Orphans} file mồ côi.",
                rows, keepVideoDays, orphans);

        var oldPosts = await sp.GetRequiredService<MediaService>().CleanupOldPostsAsync(keepVideoDays, ct);
        if (oldPosts > 0) log.LogInformation("Đã dọn {N} bài đăng quá {Days} ngày.", oldPosts, keepVideoDays);
    }

    /// <summary>
    /// Mẻ bài đăng của hôm nay: viết một lần, rồi rải ra từng khung giờ.
    /// </summary>
    /// <remarks>
    /// Khác video ở nhịp làm việc. Cả năm bài được viết trong MỘT lượt gọi Gemini ngay ở
    /// khung giờ đầu tiên, rồi mỗi khung giờ đăng một bài. Gọi riêng từng bài đúng giờ của
    /// nó sẽ tốn 5 lượt thay vì 1, mà hạn mức của tài khoản này bị chặn đúng ở số lượt gọi.
    /// <para>Vẫn theo lối "đếm khung giờ đã qua" như video: máy tắt cả buổi sáng thì lúc bật
    /// lên nó đăng bù cho đủ số khung đã trôi qua.</para>
    /// </remarks>
    private async Task RunPostsAsync(int due, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var owner = await sp.GetRequiredService<IMediaImageRepository>().FindOwnerAsync(ct);
        if (owner is null) return;                    // kho ảnh còn rỗng

        sp.GetRequiredService<ScopeUser>().RunAs(owner.Value);
        var media = sp.GetRequiredService<MediaService>();

        if (!await media.MakePostsEnabledAsync(ct)) return;

        // Viết mẻ nếu hôm nay chưa có. Tự bỏ qua khi đã có, nên gọi lại bao nhiêu lần cũng
        // không sinh thêm bài.
        try
        {
            var written = await media.WriteTodaysPostsAsync(ct);
            if (written > 0) log.LogInformation("Đã viết {N} bài đăng cho hôm nay (một lượt gọi).", written);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Viết mẻ bài hỏng, sẽ thử lại lượt sau.");
            return;
        }

        if (!await media.AutoPostEnabledAsync(ct)) return;

        var posted = await media.PostsMadeTodayAsync(ct);
        var todo = Math.Min(due, MediaService.PostsPerDay) - posted;

        for (var i = 0; i < todo && !ct.IsCancellationRequested; i++)
        {
            // Hết bài để đăng thì dừng — mẻ có thể ít hơn 5 nếu kho ảnh chưa đủ.
            if (!await media.PostNextAsync(ct)) break;

            log.LogInformation("Đã đăng bài {I}/{N} lên Fanpage.", i + 1, todo);
            try { await Task.Delay(TimeSpan.FromSeconds(20), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Dựng <paramref name="count"/> video, hoặc dựng cho đủ mẻ hôm nay khi để null.
    /// </summary>
    private async Task GenerateAsync(int? count, string why, CancellationToken ct, int? dueByNow = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var owner = await sp.GetRequiredService<IMediaImageRepository>().FindOwnerAsync(ct);
        if (owner is null) return;                    // kho ảnh còn rỗng, chưa có gì để dựng

        sp.GetRequiredService<ScopeUser>().RunAs(owner.Value);

        var media = sp.GetRequiredService<MediaService>();

        // Công tắc "tạo video" tắt thì bỏ qua — trừ khi có người bấm nút, vì bấm nút là ý
        // muốn rõ ràng, mạnh hơn một cài đặt mặc định.
        if (count is null && !await media.MakeVideosEnabledAsync(ct)) return;

        // Theo lịch: chỉ dựng cho ĐỦ số khung giờ đã qua, không dựng trước phần của các
        // khung giờ còn ở phía trước — mục đích của khung giờ là rải đều trong ngày.
        var todo = count ?? (dueByNow is { } due
            ? Math.Min(due - await media.MadeTodayAsync(ct), MediaService.PerDay)
            : await media.RemainingTodayAsync(ct));

        if (todo <= 0) return;

        log.LogInformation("Dựng {N} video ({Why}).", todo, why);

        for (var i = 0; i < todo && !ct.IsCancellationRequested; i++)
        {
            try { await media.GenerateOneAsync(ct); }
            catch (Exception ex)
            {
                // One bad video must not sink the rest of the batch. The row already carries
                // which stage broke and why, so there is something to look at afterwards.
                log.LogWarning(ex, "Dựng video thứ {I}/{N} hỏng, bỏ qua và làm tiếp.", i + 1, todo);
            }

            if (i < todo - 1)
            {
                try { await Task.Delay(Gap, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }
}
