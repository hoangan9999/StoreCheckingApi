using Xunit;

namespace StoreChecking.ContractTests;

/// <summary>
/// A Fact that skips itself, with an explanation, when no test database is reachable.
/// Without this the whole suite fails on a machine that simply has no PostgreSQL, which
/// makes a red run mean two very different things.
/// </summary>
public sealed class DbFactAttribute : FactAttribute
{
    public DbFactAttribute()
    {
        if (!TestDatabase.IsAvailable) Skip = TestDatabase.SkipReason;
    }
}
