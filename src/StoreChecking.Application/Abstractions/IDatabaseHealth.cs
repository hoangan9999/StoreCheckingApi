namespace StoreChecking.Application.Abstractions;

/// <summary>
/// Answers whether the database is reachable, without the caller knowing what the database
/// is. Exists so /health does not have to reach for a DbContext and drag EF Core into the
/// API project, which would undo the layering everywhere else.
/// </summary>
public interface IDatabaseHealth
{
    Task<bool> CanConnectAsync(CancellationToken ct = default);
}
