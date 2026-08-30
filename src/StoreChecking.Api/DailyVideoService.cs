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

        using var timer = new PeriodicTimer(Interval);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(ct);
                if (!await timer.WaitForNextTickAsync(ct)) return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Never let the loop die. A batch that failed today should be retried, not
                // silently stop the feature until somebody restarts the container.
                log.LogWarning(ex, "Lượt dựng video hằng ngày hỏng, sẽ thử lại lượt sau.");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var vnNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Ho_Chi_Minh");
        if (vnNow.Hour < startHour) return;

        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var owner = await sp.GetRequiredService<IMediaImageRepository>().FindOwnerAsync(ct);
        if (owner is null) return;                    // kho ảnh còn rỗng, chưa có gì để dựng

        sp.GetRequiredService<ScopeUser>().RunAs(owner.Value);

        var media = sp.GetRequiredService<MediaService>();
        var missing = await media.RemainingTodayAsync(ct);
        if (missing == 0) return;

        log.LogInformation("Hôm nay còn thiếu {N} video, bắt đầu dựng.", missing);

        for (var i = 0; i < missing && !ct.IsCancellationRequested; i++)
        {
            try { await media.GenerateOneAsync(ct); }
            catch (Exception ex)
            {
                // One bad video must not sink the rest of the batch. The row already carries
                // which stage broke and why, so there is something to look at afterwards.
                log.LogWarning(ex, "Dựng video thứ {I}/{N} hỏng, bỏ qua và làm tiếp.", i + 1, missing);
            }

            if (i < missing - 1)
            {
                try { await Task.Delay(Gap, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }
}
