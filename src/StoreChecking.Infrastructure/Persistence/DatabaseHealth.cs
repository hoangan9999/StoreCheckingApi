using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;

namespace StoreChecking.Infrastructure.Persistence;

public sealed class DatabaseHealth(AppDbContext db) : IDatabaseHealth
{
    public Task<bool> CanConnectAsync(CancellationToken ct = default) => db.Database.CanConnectAsync(ct);
}
