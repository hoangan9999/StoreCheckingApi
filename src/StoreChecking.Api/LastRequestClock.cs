namespace StoreChecking.Api;

/// <summary>
/// When the API last answered anything.
///
/// <para>Exists so that one slow response can be explained. "The API was slow" is not a
/// fact anybody can act on; "it was slow after sitting idle for 23 minutes, and 3.6 of the
/// 3.8 seconds were the database" is. /health reports this next to its own timings, so a
/// single measurement taken during a slow spell settles what to fix.</para>
/// </summary>
public sealed class LastRequestClock
{
    /// <summary>Where the idle span for the current request is kept, for the handler to read.</summary>
    public const string ItemKey = "idleBefore";

    private long _ticks = DateTime.UtcNow.Ticks;

    /// <summary>Records that a request arrived, and returns how long the API had been idle.</summary>
    public TimeSpan Mark()
    {
        var now = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Exchange(ref _ticks, now);
        return TimeSpan.FromTicks(Math.Max(now - previous, 0));
    }
}
