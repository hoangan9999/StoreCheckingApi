using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoreChecking.Api.Data;

namespace StoreChecking.ContractTests;

/// <summary>
/// Fails the build the moment a table is added without an owner filter.
/// <para>Supabase's row level security let the database itself refuse to hand back another
/// user's rows. Nothing here does that. What replaces it is one line per entity —
/// <c>HasQueryFilter(x =&gt; x.UserId == _user.Id)</c> — and a line someone has to remember
/// is not a safety net, it is a hope. With the rest of the app moving off Supabase, that
/// hope would have to hold across roughly fifteen more tables.</para>
/// <para>So the convention is checked by machine instead. A new entity mapped without a
/// filter turns this test red before the leak can ever ship.</para>
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
            "\nThêm HasQueryFilter(x => x.UserId == _user.Id) trong AppDbContext.OnModelCreating.");
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
