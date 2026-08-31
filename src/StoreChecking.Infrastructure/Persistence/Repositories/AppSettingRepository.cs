using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Repositories;

public sealed class AppSettingRepository(AppDbContext db) : IAppSettingRepository
{
    public Task<AppSetting?> FindAsync(string key, CancellationToken ct = default) =>
        db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);

    public void Add(AppSetting row) => db.AppSettings.Add(row);
}
