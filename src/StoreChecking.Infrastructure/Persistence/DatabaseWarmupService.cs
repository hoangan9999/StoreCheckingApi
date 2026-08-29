using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StoreChecking.Infrastructure.Persistence;

/// <summary>
/// Touches the database every few minutes so nobody's first request has to.
///
/// <para>Measured from outside the house: the first call after an idle spell answers in
/// about four seconds while the next ones take a few hundred milliseconds. The TLS
/// handshake accounts for a fraction of that, so the wait is on the NAS side, and it comes
/// back every time the app has been left alone for a while.</para>
///
/// <para>Whatever goes cold in that gap — the connection pool emptying, PostgreSQL's cache
/// aging out, the container's pages being reclaimed — this pays the price on a timer
/// instead of leaving it for whoever opens the app next. That makes it worth having even
/// before the cause is pinned down, and the timings it logs are how it gets pinned down:
/// a slow line here, at a known idle gap, says more than a slow request nobody measured.
/// </para>
///
/// <para>The query reads a real table rather than <c>select 1</c>, so it exercises the path
/// a real read takes: planner, buffers, and the storage underneath if it has gone to sleep.
/// </para>
/// </summary>
public sealed class DatabaseWarmupService(
    IServiceScopeFactory scopes,
    ILogger<DatabaseWarmupService> log,
    TimeSpan interval) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (interval <= TimeSpan.Zero)
        {
            log.LogInformation("Không hâm nóng database định kỳ (đã tắt bằng cấu hình).");
            return;
        }

        log.LogInformation("Hâm nóng database mỗi {Phut} phút.", interval.TotalMinutes);

        using var timer = new PeriodicTimer(interval);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct)) return;
                await WarmAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;                     // shutting down, nothing to report
            }
            catch (Exception ex)
            {
                // Never let this loop die. It is a background nicety, and an API that stops
                // warming itself is far better than one that stops answering.
                log.LogWarning(ex, "Hâm nóng database hỏng, sẽ thử lại ở lượt sau.");
            }
        }
    }

    private async Task WarmAsync(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("select count(*) from public.schema_history", ct);

        var ms = clock.ElapsedMilliseconds;

        // Loud only when it is slow. A line every few minutes saying "still fast" would bury
        // the ones worth reading, and these logs are capped at 5 MB.
        if (ms >= 1000) log.LogInformation("Hâm nóng database mất {Ms} ms — lượt này bị lạnh.", ms);
        else log.LogDebug("Hâm nóng database mất {Ms} ms.", ms);
    }
}
