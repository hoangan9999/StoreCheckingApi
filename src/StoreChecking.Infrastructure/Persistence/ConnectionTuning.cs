using Npgsql;

namespace StoreChecking.Infrastructure.Persistence;

/// <summary>
/// Pool settings for the connection string.
///
/// <para>Npgsql's defaults suit a server that is always busy: it keeps no connections at
/// all when idle (<c>Minimum Pool Size=0</c>) and closes anything unused after five minutes
/// (<c>Connection Idle Lifetime=300</c>). This app is idle nearly all day and then used in
/// short bursts, so under those defaults every session starts by rebuilding a connection
/// from nothing — the one request a person actually waits for.</para>
///
/// <para>Anything set explicitly in the connection string wins. These are defaults for
/// values nobody has chosen, not an override.</para>
/// </summary>
public static class ConnectionTuning
{
    /// <summary>Connections kept open through idle spells.</summary>
    private const int WarmConnections = 2;

    public static string WithWarmPool(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);

        if (!b.ContainsKey("Minimum Pool Size")) b.MinPoolSize = WarmConnections;

        // Npgsql mặc định cho tối đa 100 kết nối, trong khi PostgreSQL trên máy này chỉ
        // nhận 20 (xem docker-compose.yml) và mỗi kết nối là một tiến trình riêng. Để
        // nguyên mặc định thì khi có tải, pool sẽ mở tới lúc database từ chối — nên trần
        // ở đây phải nằm dưới trần bên kia.
        if (!b.ContainsKey("Maximum Pool Size")) b.MaxPoolSize = 10;

        // 0 = never close an idle connection. Safe because the pool checks a connection
        // before handing it out and replaces a dead one, and because the warm-up service
        // exercises the pool every few minutes, which finds a broken connection before
        // somebody's request does.
        if (!b.ContainsKey("Connection Idle Lifetime")) b.ConnectionIdleLifetime = 0;

        // Notices a connection the database dropped without telling us — after a restart
        // of the db container, say — instead of leaving it in the pool to fail later.
        if (!b.ContainsKey("Keepalive")) b.KeepAlive = 60;

        return b.ConnectionString;
    }
}
