using System.Net;
using System.Text.Json;

namespace StoreChecking.ContractTests;

/// <summary>
/// The backup endpoint reads whole tables with raw SQL, which is the ONE place in the code
/// that does not go through EF's owner filter. These tests are what stands in for it.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class BackupContractTests(ApiFactory api)
{
    private HttpClient NewUser() => api.ClientFor(Guid.NewGuid());

    /// <summary>Exactly the eight tables the previous Supabase implementation read.</summary>
    private static readonly string[] Expected =
    [
        "batches", "products", "sales", "product_damages",
        "expense_categories", "expenses", "monthly_income", "packing_videos",
    ];

    [DbFact]
    public async Task Tra_dung_tam_bang_ke_ca_khi_chua_co_du_lieu()
    {
        var res = await NewUser().GetAsync("/api/backup");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json.Read(res);

        Json.HasExactly(body, "counts", "tables");
        Json.HasExactly(body.GetProperty("counts"), Expected);
        Json.HasExactly(body.GetProperty("tables"), Expected);

        // An empty table is [], not null — the old implementation returned `data ?? []`.
        foreach (var t in Expected)
        {
            Assert.Equal(JsonValueKind.Array, body.GetProperty("tables").GetProperty(t).ValueKind);
            Assert.Equal(0, body.GetProperty("tables").GetProperty(t).GetArrayLength());
            Assert.Equal(0, body.GetProperty("counts").GetProperty(t).GetInt32());
        }
    }

    // Rows come back with the DATABASE's column names, not the API's camelCase DTO names.
    // Backup files written before the migration have that shape, and a backup format that
    // changes quietly is a backup nobody can restore from.
    [DbFact]
    public async Task Dong_du_lieu_giu_nguyen_ten_cot_cua_database()
    {
        var c = NewUser();

        await c.PostJson("/api/inventory/batches", new
        {
            name = "Lô sao lưu", importDate = "2026-08-01", totalCost = 1_000m, note = "ghi chú",
            products = new[] { new { name = "SP1", quantity = 4, sellPrice = 250m } },
        });

        var body = await Json.Read(await c.GetAsync("/api/backup"));
        var batch = body.GetProperty("tables").GetProperty("batches")[0];

        // snake_case, and user_id is present exactly as it is stored.
        foreach (var col in new[] { "id", "user_id", "name", "import_date", "total_cost", "note", "priority", "created_at" })
            Assert.True(batch.TryGetProperty(col, out _), $"Thiếu cột '{col}' trong bản sao lưu.");

        Assert.Equal("Lô sao lưu", batch.GetProperty("name").GetString());
        Assert.Equal("2026-08-01", batch.GetProperty("import_date").GetString());

        var product = body.GetProperty("tables").GetProperty("products")[0];
        foreach (var col in new[] { "id", "user_id", "batch_id", "name", "quantity", "sell_price", "created_at" })
            Assert.True(product.TryGetProperty(col, out _), $"Thiếu cột '{col}' trong bảng products.");
    }

    [DbFact]
    public async Task Counts_khop_voi_so_dong_thuc_te()
    {
        var c = NewUser();

        await c.PostJson("/api/expenses/categories", new { name = "Ăn uống", type = "variable" });
        await c.PostJson("/api/expenses/categories", new { name = "Xăng xe", type = "fixed" });
        await c.PostJson("/api/packing", new { orderCode = "DON-1", ext = "mp4" });

        var body = await Json.Read(await c.GetAsync("/api/backup"));
        var counts = body.GetProperty("counts");
        var tables = body.GetProperty("tables");

        Assert.Equal(2, counts.GetProperty("expense_categories").GetInt32());
        Assert.Equal(1, counts.GetProperty("packing_videos").GetInt32());
        Assert.Equal(0, counts.GetProperty("sales").GetInt32());

        foreach (var t in Expected)
            Assert.Equal(tables.GetProperty(t).GetArrayLength(), counts.GetProperty(t).GetInt32());
    }

    [DbFact]
    public async Task Khong_co_token_thi_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.AnonymousClient().GetAsync("/api/backup")).StatusCode);
    }
}
