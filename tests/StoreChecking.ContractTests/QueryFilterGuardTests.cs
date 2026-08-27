using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoreChecking.Infrastructure.Persistence;

namespace StoreChecking.ContractTests;

/// <summary>
/// Fails the build the moment a table is added without an owner filter.
/// <para>Supabase's row level security let the database itself refuse to hand back another
/// user's rows. Nothing here does that. AppDbContext applies the replacement automatically
/// to every entity implementing <c>IOwnedByUser</c>, so the usual way to get this wrong —
/// forgetting a HasQueryFilter line — is gone.</para>
/// <para>These tests cover the way that is left: an entity that simply never implemented
/// the interface. The generation step cannot notice that; a test comparing the finished
/// model against reality can. With roughly fifteen more tables arriving as the rest of the
/// app moves off Supabase, the difference matters.</para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class QueryFilterGuardTests(ApiFactory api)
{
    [DbFact]
    public void Moi_bang_deu_phai_co_query_filter_theo_chu_so_huu()
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unprotected = db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .Where(e => e.GetQueryFilter() is null)
            .Select(e => $"{e.ClrType.Name} -> bảng '{e.GetTableName()}'")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(unprotected.Count == 0,
            "Những entity sau chưa có HasQueryFilter, nghĩa là truy vấn quên Where sẽ trả " +
            "dữ liệu của người khác:\n  " + string.Join("\n  ", unprotected) +
            "\nCho entity đó implement IOwnedByUser là xong — AppDbContext tự gắn filter.");
    }

    /// <summary>
    /// The filter is only worth anything if it names the current user. An entity filtered
    /// by something else — a soft-delete flag, say — would pass the test above while
    /// leaking every row.
    /// </summary>
    [DbFact]
    public void Query_filter_phai_loc_theo_user_hien_tai()
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var wrong = db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .Where(e => e.GetQueryFilter() is { } f && !f.ToString().Contains("UserId"))
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.True(wrong.Count == 0,
            "Những entity sau có query filter nhưng không lọc theo UserId: " + string.Join(", ", wrong));
    }
}
