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
    int startHour,
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

        log.LogInformation("Tự dựng {N} video mỗi ngày, bắt đầu từ {Hour}h.", MediaService.PerDay, startHour);

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

    /// <summary>Lượt kiểm định kỳ: tới giờ chưa, và hôm nay còn thiếu mấy video.</summary>
    private async Task RunDailyAsync(CancellationToken ct)
    {
        var vnNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Ho_Chi_Minh");
        if (vnNow.Hour < startHour) return;

        await GenerateAsync(null, "theo lịch", ct);
    }

    /// <summary>
    /// Dựng <paramref name="count"/> video, hoặc dựng cho đủ mẻ hôm nay khi để null.
    /// </summary>
    private async Task GenerateAsync(int? count, string why, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var owner = await sp.GetRequiredService<IMediaImageRepository>().FindOwnerAsync(ct);
        if (owner is null) return;                    // kho ảnh còn rỗng, chưa có gì để dựng

        sp.GetRequiredService<ScopeUser>().RunAs(owner.Value);

        var media = sp.GetRequiredService<MediaService>();
        var todo = count ?? await media.RemainingTodayAsync(ct);
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
