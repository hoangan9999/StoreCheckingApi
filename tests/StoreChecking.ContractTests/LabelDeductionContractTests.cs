using System.Net;
using System.Text.Json;

namespace StoreChecking.ContractTests;

/// <summary>
/// Trừ tồn kho từ danh sách món đọc trên nhãn vận đơn.
///
/// <para>Thứ đang được giữ ở đây là: <b>không bao giờ đoán, và không bao giờ trừ hai
/// lần</b>. Trừ nhầm mặt hàng hoặc trừ lặp đều không có gì báo lỗi — chỉ lộ ra khi kiểm
/// kho lệch, mà lúc đó không truy lại được đơn nào gây ra.</para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class LabelDeductionContractTests(ApiFactory api)
{
    private HttpClient NewUser() => api.ClientFor(Guid.NewGuid());

    /// <summary>Nhãn Protect thật, nguyên văn như pdf.js đọc ra.</summary>
    private const string Protect = "Protect Bảo Vệ Card Hotwheels (không có xe )";
    private const string Combo2 = "Protect Basic (Combo 2 cái )";

    /// <summary>Tạo một lô một mặt hàng, trả về id lô và id mặt hàng.</summary>
    private static async Task<(Guid Batch, Guid Kind)> NewBatch(
        HttpClient c, string product = "Basic", int quantity = 100,
        decimal price = 11000m, string name = "Lô A", string importDate = "2026-08-01",
        int? priority = null)
    {
        var created = await Json.Read(await c.PostJson("/api/inventory/batches", new
        {
            name,
            importDate,
            totalCost = 500m,
            note = (string?)null,
            products = new[] { new { name = product, quantity, sellPrice = price } },
        }));
        var batchId = created.GetProperty("id").GetGuid();

        if (priority is int p)
        {
            await c.PutJson("/api/inventory/batches/priorities", new
            {
                items = new[] { new { id = batchId, priority = p } },
            });
        }

        var kinds = await Json.Read(await c.GetAsync("/api/inventory/kinds"));
        var kind = kinds.EnumerateArray().First(k => k.GetProperty("name").GetString() == product);
        return (batchId, kind.GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> MapAlias(
        HttpClient c, Guid? kindId, int units, string sourceName = Protect, string variant = Combo2) =>
        c.PutJson("/api/inventory/label-aliases", new { sourceName, variant, kindId, units });

    private static Task<HttpResponseMessage> Deduct(
        HttpClient c, string orderCode, int qty = 1,
        string name = Protect, string variant = Combo2) =>
        c.PostJson("/api/inventory/deduct-from-label", new
        {
            orderCode,
            items = new[] { new { name, variant, qty } },
        });

    /// <summary>Tổng tồn còn lại của một mặt hàng, đọc qua API chứ không qua database.</summary>
    private static async Task<long> Remaining(HttpClient c, Guid kindId)
    {
        var kinds = await Json.Read(await c.GetAsync("/api/inventory/kinds"));
        return kinds.EnumerateArray()
            .First(k => k.GetProperty("id").GetGuid() == kindId)
            .GetProperty("remaining").GetInt64();
    }

    // ---------- Không ghép thì không trừ ----------

    // Chưa ghép là KHÔNG trừ gì, và cũng không đánh dấu đơn đã trừ -- phải để ghép xong
    // còn trừ lại được. Đánh dấu ở đây là mất luôn đơn đó.
    [DbFact]
    public async Task Chua_ghep_thi_khong_tru_gi_va_khong_danh_dau()
    {
        var c = NewUser();
        var (_, kind) = await NewBatch(c);

        var first = await Json.Read(await Deduct(c, "TTVN1125622249"));

        Assert.False(first.GetProperty("deducted").GetBoolean());
        Assert.False(first.GetProperty("alreadyDone").GetBoolean());
        Assert.Equal(1, first.GetProperty("unmapped").GetArrayLength());
        Assert.Equal(100, await Remaining(c, kind));

        // Ghép xong thì đúng cái đơn đó trừ được -- chứng minh lần trước không đánh dấu.
        Assert.Equal(HttpStatusCode.OK, (await MapAlias(c, kind, 2)).StatusCode);
        var again = await Json.Read(await Deduct(c, "TTVN1125622249"));

        Assert.True(again.GetProperty("deducted").GetBoolean());
        Assert.Equal(98, await Remaining(c, kind));
    }

    // ---------- Hệ số nhân ----------

    // "Combo 2 cái" x 2 món = 4 cái. Thiếu hệ số là trừ 2, con số vẫn trông hợp lý.
    [DbFact]
    public async Task He_so_nhan_combo_tinh_dung_so_cai()
    {
        var c = NewUser();
        var (_, kind) = await NewBatch(c);
        await MapAlias(c, kind, 2);

        var body = await Json.Read(await Deduct(c, "TTVN1124614633", qty: 2));

        Assert.True(body.GetProperty("deducted").GetBoolean());
        Assert.Equal(4, body.GetProperty("unitsTotal").GetInt32());
        Assert.Equal(96, await Remaining(c, kind));
    }

    // ---------- Trừ hai lần ----------

    [DbFact]
    public async Task Goi_lai_cung_ma_don_thi_khong_tru_them()
    {
        var c = NewUser();
        var (_, kind) = await NewBatch(c);
        await MapAlias(c, kind, 2);

        await Deduct(c, "TTVN1121248540", qty: 2);
        Assert.Equal(96, await Remaining(c, kind));

        var second = await Json.Read(await Deduct(c, "TTVN1121248540", qty: 2));

        Assert.True(second.GetProperty("alreadyDone").GetBoolean());
        Assert.False(second.GetProperty("deducted").GetBoolean());
        Assert.Equal(96, await Remaining(c, kind));
    }

    // ---------- Bỏ qua ----------

    // Xe Hot Wheels không theo dõi tồn. Đánh dấu bỏ qua một lần là nó thôi nằm trong
    // "chưa ghép" -- không có hàng này thì danh sách chưa ghép đầy đơn xe và thành vô dụng.
    [DbFact]
    public async Task Danh_dau_bo_qua_thi_khong_con_bao_chua_ghep()
    {
        var c = NewUser();
        var (_, kind) = await NewBatch(c);
        await MapAlias(c, kindId: null, units: 1,
            sourceName: "Hot Wheels chính hãng - bộ sưu tập đa dạng", variant: "Ferrari SF90 Xám (2025)");

        var body = await Json.Read(await Deduct(c, "TTVN1128022049",
            name: "Hot Wheels chính hãng - bộ sưu tập đa dạng", variant: "Ferrari SF90 Xám (2025)"));

        Assert.Empty(body.GetProperty("unmapped").EnumerateArray());
        Assert.Equal(1, body.GetProperty("ignored").GetArrayLength());
        Assert.False(body.GetProperty("deducted").GetBoolean());
        Assert.Equal(100, await Remaining(c, kind));
    }

    // ---------- Thiếu tồn ----------

    // Thiếu tồn thì KHÔNG trừ gì cả. Trừ một phần rồi đánh dấu xong là che mất chuyện tồn
    // đã sai từ trước, và không trừ lại được sau khi sửa.
    [DbFact]
    public async Task Thieu_ton_thi_khong_tru_gi_ca()
    {
        var c = NewUser();
        var (_, kind) = await NewBatch(c, quantity: 3);
        await MapAlias(c, kind, 2);

        var res = await Deduct(c, "TTVN1129362449", qty: 2);   // cần 4, còn 3

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(3, await Remaining(c, kind));

        // Và đơn đó vẫn trừ lại được sau khi tồn đủ.
        await c.PostJson($"/api/inventory/batches/{(await NewBatch(c, name: "Lô B")).Batch}/products",
            new { name = "Basic", quantity = 10, sellPrice = 11000m });
        var ok = await Json.Read(await Deduct(c, "TTVN1129362449", qty: 2));
        Assert.True(ok.GetProperty("deducted").GetBoolean());
    }

    // ---------- Nhiều lô ----------

    // Cùng một tên ở hai lô phải là MỘT mặt hàng, nếu không thì ghép một lần không phủ
    // được cả hai và mỗi lô lại phải ghép lại.
    [DbFact]
    public async Task Hai_lo_cung_ten_thi_chung_mot_mat_hang()
    {
        var c = NewUser();
        var (_, kind) = await NewBatch(c, quantity: 2, name: "Lô A", priority: 1);
        var (batchB, kindB) = await NewBatch(c, quantity: 50, name: "Lô B", priority: 2);

        Assert.Equal(kind, kindB);
        Assert.Equal(52, await Remaining(c, kind));

        await MapAlias(c, kind, 1);
        var body = await Json.Read(await Deduct(c, "TTVN1123777725", qty: 5));

        // Lô ưu tiên 1 chỉ còn 2 nên phải lấy tiếp 3 cái từ lô ưu tiên 2.
        Assert.True(body.GetProperty("deducted").GetBoolean());
        Assert.Equal(5, body.GetProperty("unitsTotal").GetInt32());
        Assert.Equal(47, await Remaining(c, kind));

        var stock = await Json.Read(await c.GetAsync($"/api/inventory/batches/{batchB}/products"));
        Assert.Equal(47, stock[0].GetProperty("remaining").GetInt64());
    }

    // ---------- Chuẩn hoá ----------

    // Khoá tra cứu cắt khoảng trắng và hạ chữ thường: kho thật đang có cả 'Basic' lẫn
    // 'Basic ', nên so chữ thô là hai thứ khác nhau.
    [DbFact]
    public async Task Khac_hoa_thuong_va_khoang_trang_van_khop()
    {
        var c = NewUser();
        var (_, kind) = await NewBatch(c);
        await MapAlias(c, kind, 2, sourceName: "  PROTECT  Bảo Vệ Card Hotwheels (không có xe )  ",
            variant: "Protect   BASIC (Combo 2 cái )");

        var body = await Json.Read(await Deduct(c, "TTVN1125634333", qty: 1));

        Assert.True(body.GetProperty("deducted").GetBoolean());
        Assert.Equal(98, await Remaining(c, kind));
    }

    // ---------- Ghép lại ----------

    // Màn ghép phải sửa được bút danh ghép sai, không chỉ thêm mới -- nên là ghi đè theo
    // (sourceName, variant) chứ không phải chèn thêm dòng.
    [DbFact]
    public async Task Ghep_lai_cung_mot_chu_thi_sua_chu_khong_them_dong()
    {
        var c = NewUser();
        var (_, kind) = await NewBatch(c);

        await MapAlias(c, kind, 2);
        await MapAlias(c, kind, 10);

        var rows = await Json.Read(await c.GetAsync("/api/inventory/label-aliases"));
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(10, rows[0].GetProperty("units").GetInt32());
    }

    // ---------- Hợp đồng với client ----------

    [DbFact]
    public async Task Ket_qua_tra_du_cac_truong_client_doc()
    {
        var c = NewUser();
        var (_, kind) = await NewBatch(c);
        await MapAlias(c, kind, 2);

        var body = await Json.Read(await Deduct(c, "TTVN1122917625"));

        Json.HasExactly(body,
            "orderCode", "deducted", "alreadyDone", "saleGroupId", "unitsTotal",
            "kinds", "unmapped", "ignored");
        Json.HasExactly(body.GetProperty("kinds")[0], "kindId", "kindName", "units");
    }
}
