using System.Net;
using System.Text.Json;

namespace StoreChecking.ContractTests;

/// <summary>
/// The most important tests in this suite.
/// <para>On Supabase the database itself refused to hand back another user's rows. That
/// safety net is gone here: all that stops a leak is EF Core's global query filter, which
/// is a convention someone has to remember on every new table. These tests are what turns
/// that convention into something enforced.</para>
/// <para>Every case checks BOTH directions — that the other user cannot list the row, and
/// that they cannot reach it by Id even when they know it.</para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class IsolationContractTests(ApiFactory api)
{
    [DbFact]
    public async Task Tu_vung_cua_nguoi_khac_khong_doc_duoc_va_khong_xoa_duoc()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var mine = await Json.Read(await a.PostJson("/api/english/words",
            new { word = "private", data = new { meaning = "cua rieng A" } }));
        var id = mine.GetProperty("id").GetGuid();

        var seenByB = await Json.Read(await b.GetAsync("/api/english/words"));
        Assert.Equal(0, seenByB.GetProperty("total").GetInt32());
        Assert.Equal(0, seenByB.GetProperty("items").GetArrayLength());

        // Knowing the Id must not help.
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/english/words/{id}")).StatusCode);

        // ...and the row is still there for its owner afterwards.
        Assert.Equal(1, (await Json.Read(await a.GetAsync("/api/english/words"))).GetProperty("total").GetInt32());
    }

    [DbFact]
    public async Task Cau_da_luu_cua_nguoi_khac_khong_doc_duoc_va_khong_xoa_duoc()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var mine = await Json.Read(await a.PostJson("/api/english/sentences",
            new { text = "Only A may see this.", note = (string?)null }));
        var id = mine.GetProperty("id").GetGuid();

        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/english/sentences"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/english/sentences?q=Only%20A"))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/english/sentences/{id}")).StatusCode);
    }

    [DbFact]
    public async Task O_ngay_cua_nguoi_khac_khong_doc_duoc_va_ghi_de_khong_dung_vao_dong_cua_ho()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var mine = await Json.Read(await a.PutJson("/api/work-calendar/days/2026-12-24", new { note = "cua A", color = "do" }));

        var seenByB = await Json.Read(await b.GetAsync("/api/work-calendar/days?from=2026-12-01&to=2026-12-31"));
        Assert.Equal(0, seenByB.GetArrayLength());

        // B writing the SAME date must create a separate row, not overwrite A's. The unique
        // index is on (user_id, day), so both rows can coexist.
        var theirs = await Json.Read(await b.PutJson("/api/work-calendar/days/2026-12-24", new { note = "cua B", color = "xanh" }));
        Assert.NotEqual(mine.GetProperty("id").GetGuid(), theirs.GetProperty("id").GetGuid());

        var aAgain = await Json.Read(await a.GetAsync("/api/work-calendar/days?from=2026-12-01&to=2026-12-31"));
        Assert.Equal(1, aAgain.GetArrayLength());
        Assert.Equal("cua A", aAgain[0].GetProperty("note").GetString());
    }

    [DbFact]
    public async Task Ghi_chu_thang_cua_nguoi_khac_khong_doc_sua_hay_xoa_duoc()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var mine = await Json.Read(await a.PostJson("/api/work-calendar/notes", new { period = "2026-12-01", sort = 1 }));
        var id = mine.GetProperty("id").GetGuid();
        await a.PutJson($"/api/work-calendar/notes/{id}", new { content = "bi mat cua A" });

        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/work-calendar/notes?period=2026-12-01"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutJson($"/api/work-calendar/notes/{id}", new { content = "B sua trom" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/work-calendar/notes/{id}")).StatusCode);

        // Untouched.
        var still = await Json.Read(await a.GetAsync("/api/work-calendar/notes?period=2026-12-01"));
        Assert.Equal("bi mat cua A", still[0].GetProperty("content").GetString());
    }

    [DbFact]
    public async Task Ghi_chu_nhanh_cua_nguoi_khac_khong_doc_sua_hay_xoa_duoc()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var mine = await Json.Read(await a.PostJson("/api/notes",
            new { title = "STK cua A", content = "0123456789" }));
        var id = mine.GetProperty("id").GetGuid();

        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/notes"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound,
            (await b.PutJson($"/api/notes/{id}", new { title = "B sua trom", content = "9999" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/notes/{id}")).StatusCode);

        // Untouched.
        var still = await Json.Read(await a.GetAsync("/api/notes"));
        Assert.Equal(1, still.GetArrayLength());
        Assert.Equal("0123456789", still[0].GetProperty("content").GetString());
    }

    [DbFact]
    public async Task Nhat_ky_dong_goi_cua_nguoi_khac_khong_doc_hay_xoa_duoc()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        await a.PostJson("/api/packing", new { orderCode = "RIENG-CUA-A", ext = "mp4" });

        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/packing"))).GetArrayLength());
        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/packing?search=RIENG"))).GetArrayLength());
        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/packing/filenames"))).GetArrayLength());

        var mine = await Json.Read(await a.GetAsync("/api/packing"));
        var id = mine[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/packing/{id}")).StatusCode);

        Assert.Equal(1, (await Json.Read(await a.GetAsync("/api/packing"))).GetArrayLength());
    }

    // Seq is worked out per owner. Two people filming the same order code must not have
    // their file names collide on the NAS... and must not see each other's count either.
    [DbFact]
    public async Task Seq_dong_goi_tinh_rieng_cho_tung_nguoi()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        await a.PostJson("/api/packing", new { orderCode = "CHUNG-MA", ext = "mp4" });
        await a.PostJson("/api/packing", new { orderCode = "CHUNG-MA", ext = "mp4" });

        var theirs = await Json.Read(await b.PostJson("/api/packing", new { orderCode = "CHUNG-MA", ext = "mp4" }));
        Assert.Equal(1, theirs.GetProperty("seq").GetInt32());
    }

    [DbFact]
    public async Task Chi_tieu_cua_nguoi_khac_khong_doc_sua_hay_xoa_duoc()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var cat = (await Json.Read(await a.PostJson("/api/expenses/categories",
            new { name = "Của A", type = "variable" }))).GetProperty("id").GetGuid();
        var spend = (await Json.Read(await a.PostJson("/api/expenses",
            new { categoryId = cat, spentOn = "2026-08-15", description = "bí mật", amount = 500m, note = (string?)null })))
            .GetProperty("id").GetGuid();
        await a.PutJson("/api/expenses/income", new { year = 2026, month = 8, income = 9_000m, note = (string?)null });

        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/expenses/categories"))).GetArrayLength());
        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/expenses?year=2026&month=8"))).GetArrayLength());
        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/expenses/income?year=2026"))).GetArrayLength());

        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/expenses/categories/{cat}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/expenses/{spend}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutJson($"/api/expenses/{spend}",
            new { categoryId = cat, spentOn = "2026-08-16", description = "B sửa trộm", amount = 1m, note = (string?)null })).StatusCode);

        // A's own data untouched.
        Assert.Equal(1, (await Json.Read(await a.GetAsync("/api/expenses?year=2026&month=8"))).GetArrayLength());
    }

    // Views need the owner filter as much as tables do, and it is easier to forget there:
    // there is no key, nothing is ever written, and a leak would look like a rounding
    // error in someone else's chart rather than like a security hole.
    [DbFact]
    public async Task Hai_view_tong_hop_cung_bi_loc_theo_chu_so_huu()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var cat = (await Json.Read(await a.PostJson("/api/expenses/categories",
            new { name = "Của A", type = "variable" }))).GetProperty("id").GetGuid();
        await a.PostJson("/api/expenses",
            new { categoryId = cat, spentOn = "2026-08-15", description = (string?)null, amount = 777m, note = (string?)null });

        // A sees the roll-up…
        var mineByCat = await Json.Read(await a.GetAsync("/api/expenses/summary/categories?year=2026&month=8"));
        Assert.Equal(1, mineByCat.GetArrayLength());
        Assert.Equal(777m, mineByCat[0].GetProperty("spent").GetDecimal());

        // …and B sees nothing at all, in either view.
        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/expenses/summary/categories?year=2026&month=8"))).GetArrayLength());
        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/expenses/summary/months?year=2026"))).GetArrayLength());
    }

    // Pointing an expense at someone else's category must fail as "not found", not succeed:
    // succeeding would both corrupt their totals and confirm that the category exists.
    [DbFact]
    public async Task Khong_the_gan_giao_dich_vao_danh_muc_cua_nguoi_khac()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var cat = (await Json.Read(await a.PostJson("/api/expenses/categories",
            new { name = "Của A", type = "variable" }))).GetProperty("id").GetGuid();

        var res = await b.PostJson("/api/expenses",
            new { categoryId = cat, spentOn = "2026-08-15", description = (string?)null, amount = 1m, note = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Danh mục không tồn tại.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    // The inventory is the most valuable data in the application, and it spans four tables
    // plus two views. Everything here has to be invisible to anyone else.
    [DbFact]
    public async Task Kho_hang_cua_nguoi_khac_khong_doc_duoc_o_bat_ky_dau()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var batch = (await Json.Read(await a.PostJson("/api/inventory/batches", new
        {
            name = "Lô của A", importDate = "2026-08-01", totalCost = 500m, note = (string?)null,
            products = new[] { new { name = "SP của A", quantity = 10, sellPrice = 100m } },
        }))).GetProperty("id").GetGuid();

        var product = (await Json.Read(await a.GetAsync($"/api/inventory/batches/{batch}/products")))[0]
            .GetProperty("id").GetGuid();

        await a.PostJson("/api/inventory/sales", new
        {
            items = new[] { new { productId = product, quantity = 2, sellPrice = 100m } },
            soldAt = "2026-08-15T10:00:00Z", shippingFee = 0m, note = (string?)null,
        });
        await a.PostJson("/api/inventory/damages", new { productId = product, quantity = 1, note = (string?)null });

        // Tables and both views: B sees nothing at all.
        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/inventory/batches"))).GetArrayLength());
        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/inventory/stock"))).GetArrayLength());
        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/inventory/sales"))).GetArrayLength());

        // Nor by knowing the ids.
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/inventory/batches/{batch}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/inventory/batches/{batch}/products")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/inventory/batches/{batch}/sales")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/inventory/batches/{batch}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/inventory/products/{product}")).StatusCode);

        // A's batch is untouched.
        Assert.Equal(1, (await Json.Read(await a.GetAsync("/api/inventory/batches"))).GetArrayLength());
    }

    // Selling someone else's stock would both steal from their figures and confirm that the
    // product exists. It has to read as "not there", not as a permission error.
    [DbFact]
    public async Task Khong_the_ban_hoac_ghi_hu_san_pham_cua_nguoi_khac()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var batch = (await Json.Read(await a.PostJson("/api/inventory/batches", new
        {
            name = "Lô của A", importDate = "2026-08-01", totalCost = 0m, note = (string?)null,
            products = new[] { new { name = "SP của A", quantity = 10, sellPrice = 100m } },
        }))).GetProperty("id").GetGuid();

        var product = (await Json.Read(await a.GetAsync($"/api/inventory/batches/{batch}/products")))[0]
            .GetProperty("id").GetGuid();

        var sell = await b.PostJson("/api/inventory/sales", new
        {
            items = new[] { new { productId = product, quantity = 1, sellPrice = 100m } },
            soldAt = "2026-08-15T10:00:00Z", shippingFee = 0m, note = (string?)null,
        });
        Assert.Equal(HttpStatusCode.BadRequest, sell.StatusCode);
        Assert.Equal("Sản phẩm không tồn tại.", (await Json.Read(sell)).GetProperty("error").GetString());

        var damage = await b.PostJson("/api/inventory/damages", new { productId = product, quantity = 1, note = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, damage.StatusCode);
        Assert.Equal("Sản phẩm không tồn tại.", (await Json.Read(damage)).GetProperty("error").GetString());

        // A's stock is exactly as it was.
        var stock = await Json.Read(await a.GetAsync("/api/inventory/stock"));
        Assert.Equal(10, stock[0].GetProperty("remaining").GetInt64());
    }

    // Reordering takes a list of ids. Ids belonging to someone else must be ignored rather
    // than applied — silently, since reporting them would confirm they exist.
    [DbFact]
    public async Task Dat_uu_tien_bo_qua_lo_khong_phai_cua_minh()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var theirs = (await Json.Read(await a.PostJson("/api/inventory/batches", new
        {
            name = "Lô của A", importDate = "2026-08-01", totalCost = 0m,
            note = (string?)null, products = Array.Empty<object>(),
        }))).GetProperty("id").GetGuid();

        var res = await b.PutJson("/api/inventory/batches/priorities",
            new { items = new[] { new { id = theirs, priority = 1 } } });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(0, (await Json.Read(res)).GetProperty("changed").GetInt32());

        var mine = await Json.Read(await a.GetAsync("/api/inventory/batches"));
        Assert.Equal(JsonValueKind.Null, mine[0].GetProperty("priority").ValueKind);
    }

    [DbFact]
    public async Task Luu_trung_cau_chi_tinh_trong_pham_vi_mot_nguoi()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());
        const string text = "Two people can save the same sentence.";

        var first = await a.PostJson("/api/english/sentences", new { text, note = (string?)null });
        var second = await b.PostJson("/api/english/sentences", new { text, note = (string?)null });

        // The de-duplication lookup runs through the query filter, so B gets a row of
        // their own rather than being handed A's.
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(
            (await Json.Read(first)).GetProperty("id").GetGuid(),
            (await Json.Read(second)).GetProperty("id").GetGuid());
    }
}
