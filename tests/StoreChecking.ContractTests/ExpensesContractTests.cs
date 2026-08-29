using System.Net;
using System.Text.Json;

namespace StoreChecking.ContractTests;

[Collection(nameof(ApiCollection))]
public sealed class ExpensesContractTests(ApiFactory api)
{
    private HttpClient NewUser() => api.ClientFor(Guid.NewGuid());

    /// <summary>Creates a category and returns its id — the starting point for nearly every case here.</summary>
    private static async Task<Guid> NewCategory(HttpClient c, string name = "Ăn uống", int? sort = null)
    {
        var res = await c.PostJson("/api/expenses/categories", new
        {
            name,
            monthlyBudget = 5_000_000m,
            type = "variable",
            icon = "🍜",
            dailyLimit = (decimal?)null,
            note = (string?)null,
            sortOrder = sort,
        });
        return (await Json.Read(res)).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> Spend(
        HttpClient c, Guid categoryId, string on, decimal amount, string? description = null) =>
        c.PostJson("/api/expenses", new { categoryId, spentOn = on, description, amount, note = (string?)null });

    // ---------- Danh mục ----------

    [DbFact]
    public async Task Them_danh_muc_roi_doc_lai()
    {
        var c = NewUser();

        var res = await c.PostJson("/api/expenses/categories", new
        {
            name = "  Xăng xe  ",
            monthlyBudget = 1_200_000m,
            type = "fixed",
            icon = "⛽",
            dailyLimit = 50_000m,
            note = "  đổ đầy bình  ",
            sortOrder = (int?)null,
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var cat = await Json.Read(res);
        Json.HasExactly(cat, "id", "name", "monthlyBudget", "type", "icon", "dailyLimit", "note", "sortOrder");
        Assert.Equal("Xăng xe", cat.GetProperty("name").GetString());       // trimmed
        Assert.Equal("fixed", cat.GetProperty("type").GetString());
        Assert.Equal("đổ đầy bình", cat.GetProperty("note").GetString());
        Assert.Equal(1_200_000m, cat.GetProperty("monthlyBudget").GetDecimal());
        Assert.Equal(1, cat.GetProperty("sortOrder").GetInt32());           // first one, so 1
    }

    [DbFact]
    public async Task Khong_dua_sortOrder_thi_danh_muc_moi_xep_cuoi()
    {
        var c = NewUser();
        await NewCategory(c, "A");
        await NewCategory(c, "B");
        await NewCategory(c, "C");

        var listed = await Json.Read(await c.GetAsync("/api/expenses/categories"));
        Assert.Equal([1, 2, 3], listed.EnumerateArray().Select(x => x.GetProperty("sortOrder").GetInt32()).ToArray());
        Assert.Equal(["A", "B", "C"], listed.EnumerateArray().Select(x => x.GetProperty("name").GetString()!).ToArray());
    }

    [DbFact]
    public async Task Danh_muc_sap_theo_sortOrder_chu_khong_theo_luc_tao()
    {
        var c = NewUser();
        await NewCategory(c, "Cuối", 9);
        await NewCategory(c, "Đầu", 1);

        var listed = await Json.Read(await c.GetAsync("/api/expenses/categories"));
        Assert.Equal("Đầu", listed[0].GetProperty("name").GetString());
        Assert.Equal("Cuối", listed[1].GetProperty("name").GetString());
    }

    [DbFact]
    public async Task Ten_danh_muc_rong_thi_400()
    {
        var res = await NewUser().PostJson("/api/expenses/categories", new { name = "   ", type = "variable" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Thiếu tên danh mục.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    // The database has a check constraint for this; the message is nicer than its error.
    [DbFact]
    public async Task Loai_danh_muc_la_thu_khac_thi_400()
    {
        var res = await NewUser().PostJson("/api/expenses/categories", new { name = "Linh tinh", type = "khac" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Loại danh mục phải là 'fixed' hoặc 'variable'.",
            (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Khong_dua_loai_thi_mac_dinh_variable()
    {
        var c = NewUser();
        var cat = await Json.Read(await c.PostJson("/api/expenses/categories",
            new { name = "Không loại", type = (string?)null }));

        Assert.Equal("variable", cat.GetProperty("type").GetString());
    }

    [DbFact]
    public async Task Sua_danh_muc_la_thay_the_toan_phan()
    {
        var c = NewUser();
        var id = await NewCategory(c, "Cũ");

        var res = await c.PutJson($"/api/expenses/categories/{id}", new
        {
            name = "Mới",
            monthlyBudget = (decimal?)null,
            type = "fixed",
            icon = (string?)null,
            dailyLimit = (decimal?)null,
            note = (string?)null,
            sortOrder = 7,
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var cat = await Json.Read(res);
        Assert.Equal("Mới", cat.GetProperty("name").GetString());
        Assert.Equal("fixed", cat.GetProperty("type").GetString());
        Assert.Equal(7, cat.GetProperty("sortOrder").GetInt32());
        // Fields left out really are cleared — the client edits in a dialog that sends all of them.
        Assert.Equal(JsonValueKind.Null, cat.GetProperty("monthlyBudget").ValueKind);
        Assert.Equal(JsonValueKind.Null, cat.GetProperty("icon").ValueKind);
    }

    [DbFact]
    public async Task Xoa_danh_muc_rong()
    {
        var c = NewUser();
        var id = await NewCategory(c, "Bỏ đi");

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/expenses/categories/{id}")).StatusCode);
        Assert.Equal(0, (await Json.Read(await c.GetAsync("/api/expenses/categories"))).GetArrayLength());
    }

    // `on delete restrict` in the schema would refuse this anyway; catching it here turns a
    // raw foreign key error into a sentence the user can act on.
    [DbFact]
    public async Task Xoa_danh_muc_con_giao_dich_thi_400()
    {
        var c = NewUser();
        var id = await NewCategory(c);
        await Spend(c, id, "2026-08-10", 50_000m);

        var res = await c.DeleteAsync($"/api/expenses/categories/{id}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Danh mục còn giao dịch, không xoá được. Chuyển các giao dịch sang danh mục khác trước.",
            (await Json.Read(res)).GetProperty("error").GetString());

        // Still there.
        Assert.Equal(1, (await Json.Read(await c.GetAsync("/api/expenses/categories"))).GetArrayLength());
    }

    [DbFact]
    public async Task Sua_hoac_xoa_danh_muc_khong_ton_tai_thi_404()
    {
        var c = NewUser();
        var missing = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound,
            (await c.PutJson($"/api/expenses/categories/{missing}", new { name = "x", type = "variable" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/expenses/categories/{missing}")).StatusCode);
    }

    // ---------- Giao dịch ----------

    [DbFact]
    public async Task Them_giao_dich_roi_doc_lai()
    {
        var c = NewUser();
        var cat = await NewCategory(c);

        var res = await Spend(c, cat, "2026-08-15", 125_000m, "  phở  ");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var e = await Json.Read(res);
        Json.HasExactly(e, "id", "categoryId", "spentOn", "description", "amount", "note", "createdAt");
        Assert.Equal(cat, e.GetProperty("categoryId").GetGuid());
        Assert.Equal("2026-08-15", e.GetProperty("spentOn").GetString());
        Assert.Equal("phở", e.GetProperty("description").GetString());      // trimmed
        Assert.Equal(125_000m, e.GetProperty("amount").GetDecimal());
    }

    [DbFact]
    public async Task Chi_liet_ke_dung_thang_duoc_hoi()
    {
        var c = NewUser();
        var cat = await NewCategory(c);

        await Spend(c, cat, "2026-07-31", 1m);     // tháng trước
        await Spend(c, cat, "2026-08-01", 2m);     // đầu tháng
        await Spend(c, cat, "2026-08-31", 3m);     // cuối tháng
        await Spend(c, cat, "2026-09-01", 4m);     // tháng sau

        var listed = await Json.Read(await c.GetAsync("/api/expenses?year=2026&month=8"));
        Assert.Equal(2, listed.GetArrayLength());
        Assert.Equal(["2026-08-31", "2026-08-01"],
            listed.EnumerateArray().Select(x => x.GetProperty("spentOn").GetString()!).ToArray());
    }

    // Ranges are computed as [first of month, first of NEXT month), so December has to roll
    // into the following January rather than into month 13.
    [DbFact]
    public async Task Thang_12_khong_tran_sang_thang_13()
    {
        var c = NewUser();
        var cat = await NewCategory(c);

        await Spend(c, cat, "2026-12-31", 10m);
        await Spend(c, cat, "2027-01-01", 20m);

        var dec = await Json.Read(await c.GetAsync("/api/expenses?year=2026&month=12"));
        Assert.Equal(1, dec.GetArrayLength());
        Assert.Equal("2026-12-31", dec[0].GetProperty("spentOn").GetString());

        var jan = await Json.Read(await c.GetAsync("/api/expenses?year=2027&month=1"));
        Assert.Equal(1, jan.GetArrayLength());
        Assert.Equal("2027-01-01", jan[0].GetProperty("spentOn").GetString());
    }

    [DbFact]
    public async Task Thang_ngoai_1_12_thi_400()
    {
        var c = NewUser();

        foreach (var m in new[] { 0, 13 })
        {
            var res = await c.GetAsync($"/api/expenses?year=2026&month={m}");
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal("Tháng phải từ 1 đến 12.", (await Json.Read(res)).GetProperty("error").GetString());
        }
    }

    [DbFact]
    public async Task Ngay_sai_dinh_dang_thi_400()
    {
        var c = NewUser();
        var cat = await NewCategory(c);

        var res = await Spend(c, cat, "15/08/2026", 1m);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Ngày chi phải dạng YYYY-MM-DD.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task So_tien_am_thi_400()
    {
        var c = NewUser();
        var cat = await NewCategory(c);

        var res = await Spend(c, cat, "2026-08-15", -1m);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Số tiền không được âm.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Danh_muc_khong_ton_tai_thi_400()
    {
        var c = NewUser();

        var res = await Spend(c, Guid.NewGuid(), "2026-08-15", 1m);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Danh mục không tồn tại.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Sua_va_xoa_giao_dich()
    {
        var c = NewUser();
        var cat = await NewCategory(c);
        var other = await NewCategory(c, "Khác");
        var id = (await Json.Read(await Spend(c, cat, "2026-08-15", 100m, "cũ"))).GetProperty("id").GetGuid();

        var updated = await c.PutJson($"/api/expenses/{id}", new
        {
            categoryId = other, spentOn = "2026-08-20", description = "mới", amount = 250m, note = "ghi chú",
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var e = await Json.Read(updated);
        Assert.Equal(other, e.GetProperty("categoryId").GetGuid());
        Assert.Equal("2026-08-20", e.GetProperty("spentOn").GetString());
        Assert.Equal(250m, e.GetProperty("amount").GetDecimal());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/expenses/{id}")).StatusCode);
        Assert.Equal(0, (await Json.Read(await c.GetAsync("/api/expenses?year=2026&month=8"))).GetArrayLength());
    }

    [DbFact]
    public async Task Sua_hoac_xoa_giao_dich_khong_ton_tai_thi_404()
    {
        var c = NewUser();
        var cat = await NewCategory(c);
        var missing = Guid.NewGuid();

        var put = await c.PutJson($"/api/expenses/{missing}",
            new { categoryId = cat, spentOn = "2026-08-15", description = (string?)null, amount = 1m, note = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/expenses/{missing}")).StatusCode);
    }

    // ---------- Thu nhập ----------

    [DbFact]
    public async Task Dat_thu_nhap_roi_ghi_de()
    {
        var c = NewUser();

        var first = await c.PutJson("/api/expenses/income",
            new { year = 2026, month = 8, income = 20_000_000m, note = "  lương  " });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var row = await Json.Read(first);
        Json.HasExactly(row, "id", "year", "month", "income", "note");
        Assert.Equal(20_000_000m, row.GetProperty("income").GetDecimal());
        Assert.Equal("lương", row.GetProperty("note").GetString());

        var again = await Json.Read(await c.PutJson("/api/expenses/income",
            new { year = 2026, month = 8, income = 25_000_000m, note = (string?)null }));

        // Same row, overwritten — the unique index says one per month.
        Assert.Equal(row.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
        Assert.Equal(25_000_000m, again.GetProperty("income").GetDecimal());

        var listed = await Json.Read(await c.GetAsync("/api/expenses/income?year=2026"));
        Assert.Equal(1, listed.GetArrayLength());
    }

    [DbFact]
    public async Task Thu_nhap_liet_ke_theo_nam_va_sap_theo_thang()
    {
        var c = NewUser();
        foreach (var m in new[] { 3, 1, 2 })
            await c.PutJson("/api/expenses/income", new { year = 2026, month = m, income = m * 1000m, note = (string?)null });
        await c.PutJson("/api/expenses/income", new { year = 2025, month = 5, income = 999m, note = (string?)null });

        var listed = await Json.Read(await c.GetAsync("/api/expenses/income?year=2026"));
        Assert.Equal([1, 2, 3], listed.EnumerateArray().Select(x => x.GetProperty("month").GetInt32()).ToArray());
    }

    [DbFact]
    public async Task Thu_nhap_thang_sai_thi_400()
    {
        var res = await NewUser().PutJson("/api/expenses/income",
            new { year = 2026, month = 13, income = 1m, note = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Tháng phải từ 1 đến 12.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    // ---------- Tổng hợp: hai VIEW ----------

    // First module whose data comes from views rather than tables. Postgres does the
    // grouping; these tests check that the mapping reads it back correctly.
    [DbFact]
    public async Task Tong_hop_theo_danh_muc_dung_so_va_dung_so_giao_dich()
    {
        var c = NewUser();
        var food = await NewCategory(c, "Ăn uống");
        var fuel = await NewCategory(c, "Xăng xe");

        await Spend(c, food, "2026-08-02", 100m);
        await Spend(c, food, "2026-08-09", 250m);
        await Spend(c, fuel, "2026-08-11", 400m);
        await Spend(c, food, "2026-09-01", 999m);      // tháng khác, không được tính vào

        var rows = await Json.Read(await c.GetAsync("/api/expenses/summary/categories?year=2026&month=8"));
        Assert.Equal(2, rows.GetArrayLength());

        var byId = rows.EnumerateArray().ToDictionary(x => x.GetProperty("categoryId").GetGuid());
        Json.HasExactly(byId[food], "categoryId", "year", "month", "spent", "txCount");

        Assert.Equal(350m, byId[food].GetProperty("spent").GetDecimal());
        Assert.Equal(2, byId[food].GetProperty("txCount").GetInt64());
        Assert.Equal(400m, byId[fuel].GetProperty("spent").GetDecimal());
        Assert.Equal(1, byId[fuel].GetProperty("txCount").GetInt64());
    }

    [DbFact]
    public async Task Tong_hop_theo_thang_gop_moi_danh_muc()
    {
        var c = NewUser();
        var food = await NewCategory(c, "Ăn uống");
        var fuel = await NewCategory(c, "Xăng xe");

        await Spend(c, food, "2026-08-02", 100m);
        await Spend(c, fuel, "2026-08-11", 400m);
        await Spend(c, food, "2026-10-05", 70m);
        await Spend(c, food, "2025-08-05", 5m);        // năm khác

        var rows = await Json.Read(await c.GetAsync("/api/expenses/summary/months?year=2026"));
        Assert.Equal(2, rows.GetArrayLength());
        Json.HasExactly(rows[0], "year", "month", "spent");

        // Ordered by month.
        Assert.Equal(8, rows[0].GetProperty("month").GetInt32());
        Assert.Equal(500m, rows[0].GetProperty("spent").GetDecimal());
        Assert.Equal(10, rows[1].GetProperty("month").GetInt32());
        Assert.Equal(70m, rows[1].GetProperty("spent").GetDecimal());
    }

    [DbFact]
    public async Task Thang_chua_chi_gi_thi_tong_hop_rong()
    {
        var c = NewUser();
        await NewCategory(c);

        Assert.Equal(0, (await Json.Read(await c.GetAsync("/api/expenses/summary/categories?year=2026&month=8"))).GetArrayLength());
        Assert.Equal(0, (await Json.Read(await c.GetAsync("/api/expenses/summary/months?year=2026"))).GetArrayLength());
    }

    [DbFact]
    public async Task Khong_co_token_thi_401()
    {
        var anon = api.AnonymousClient();

        foreach (var url in new[]
                 {
                     "/api/expenses/categories",
                     "/api/expenses?year=2026&month=8",
                     "/api/expenses/income?year=2026",
                     "/api/expenses/summary/categories?year=2026&month=8",
                     "/api/expenses/summary/months?year=2026",
                 })
        {
            Assert.True((await anon.GetAsync(url)).StatusCode == HttpStatusCode.Unauthorized,
                $"{url} phải trả 401 khi không có token.");
        }
    }
}
