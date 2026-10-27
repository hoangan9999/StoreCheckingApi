using System.Linq;
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

    /// <summary>
    /// Tám bảng có từ thời Supabase. Đây là mức SÀN, không phải toàn bộ — danh sách thật
    /// được dò từ database, nên bảng mới tự vào sao lưu mà không ai phải nhớ.
    /// </summary>
    private static readonly string[] MustInclude =
    [
        "batches", "products", "sales", "product_damages",
        "expense_categories", "expenses", "monthly_income", "packing_videos",
    ];

    [DbFact]
    public async Task Tra_du_bang_ke_ca_khi_chua_co_du_lieu()
    {
        var res = await NewUser().GetAsync("/api/backup");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json.Read(res);

        Json.HasExactly(body, "counts", "tables");

        // `counts` và `tables` phải khớp nhau từng khoá, nếu không thì một bên đang nói dối
        // về bên kia.
        Assert.Equal(
            body.GetProperty("counts").EnumerateObject().Select(p => p.Name).OrderBy(x => x),
            body.GetProperty("tables").EnumerateObject().Select(p => p.Name).OrderBy(x => x));

        // An empty table is [], not null — the old implementation returned `data ?? []`.
        foreach (var t in MustInclude)
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

        // Mọi bảng, không chỉ tám bảng cũ: `counts` phải khớp độ dài mảng thật của từng cái.
        foreach (var p in tables.EnumerateObject())
            Assert.Equal(p.Value.GetArrayLength(), counts.GetProperty(p.Name).GetInt32());
    }

    [DbFact]
    public async Task Khong_co_token_thi_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.AnonymousClient().GetAsync("/api/backup")).StatusCode);
    }

    /// <summary>
    /// Mọi bảng có dữ liệu theo người dùng đều phải nằm trong bản sao lưu.
    /// </summary>
    /// <remarks>
    /// Đây là test đáng giá nhất trong tệp này. Trước đây danh sách bảng ghi cứng trong
    /// code, và nó đã tụt lại phía sau database — mười ba bảng nằm ngoài bản sao lưu, trong
    /// đó có ghi chú và từ vựng tiếng Anh, mà không có dấu hiệu nào cho tới ngày cần dùng.
    /// Test này so thẳng với danh mục của database, nên thêm bảng mới mà sao lưu không theo
    /// kịp là nó đỏ ngay.
    /// </remarks>
    [DbFact]
    public async Task Sao_luu_phu_het_moi_bang_co_du_lieu_nguoi_dung()
    {
        var body = await Json.Read(await NewUser().GetAsync("/api/backup"));

        var inBackup = body.GetProperty("tables")
            .EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToList();

        var inDatabase = await TestDatabase.QueryStringsAsync(
            @"select c.table_name
              from information_schema.columns c
              join information_schema.tables t
                on t.table_schema = c.table_schema and t.table_name = c.table_name
              where c.table_schema = 'public'
                and c.column_name = 'user_id'
                and t.table_type = 'BASE TABLE'
              order by c.table_name");

        Assert.Equal(inDatabase, inBackup);

        // schema_history ghi migration nào đã chạy, không phải dữ liệu của ai — và nó không
        // có user_id nên truy vấn dump sẽ hỏng nếu lọt vào.
        Assert.DoesNotContain("schema_history", inBackup);
    }
}
