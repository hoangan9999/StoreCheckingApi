using System.Net;
using System.Text.Json;

namespace StoreChecking.ContractTests;

/// <summary>
/// Trừ tồn theo nhãn, bản người-quyết-định.
///
/// <para>Điều đang được giữ ở đây: <b>không có gì tự quyết, không trừ hai lần, và hoàn
/// lại được trong 7 ngày</b>. Trừ nhầm mặt hàng hay trừ lặp đều không kêu lên tiếng nào —
/// chỉ lộ ra lúc kiểm kho lệch, mà khi ấy không truy lại được đơn nào gây ra.</para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class LabelStockContractTests(ApiFactory api)
{
    private HttpClient NewUser() => api.ClientFor(Guid.NewGuid());

    /// <summary>Nhãn Protect thật, nguyên văn như pdf.js đọc ra.</summary>
    private const string Protect = "Protect Bảo Vệ Card Hotwheels (không có xe )";
    private const string Combo2 = "Protect Basic (Combo 2 cái )";

    /// <summary>Một lô một sản phẩm. Trả id sản phẩm — nay chọn thẳng sản phẩm, không qua mặt hàng.</summary>
    private static async Task<Guid> NewProduct(
        HttpClient c, string product = "Basic", int quantity = 100,
        decimal price = 11000m, string batch = "Lô A", string importDate = "2026-08-01")
    {
        await c.PostJson("/api/inventory/batches", new
        {
            name = batch,
            importDate,
            totalCost = 500m,
            note = (string?)null,
            products = new[] { new { name = product, quantity, sellPrice = price } },
        });

        var stock = await Json.Read(await c.GetAsync("/api/inventory/stock"));
        return stock.EnumerateArray()
            .First(x => x.GetProperty("name").GetString() == product
                     && x.GetProperty("batchName").GetString() == batch)
            .GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> Deduct(
        HttpClient c, string code, Guid productId, int quantity,
        string name = Protect, string variant = "", int labelQty = 1) =>
        c.PostJson("/api/inventory/deduct-from-label", new
        {
            orderCode = code,
            lines = new[] { new { sourceName = name, variant, labelQty, productId, quantity } },
        });

    private static Task<HttpResponseMessage> Skip(HttpClient c, params string[] codes) =>
        c.PostJson("/api/inventory/label-actions/skip", new { orderCodes = codes });

    private static async Task<long> Remaining(HttpClient c, Guid productId)
    {
        var stock = await Json.Read(await c.GetAsync("/api/inventory/stock"));
        return stock.EnumerateArray()
            .First(x => x.GetProperty("id").GetGuid() == productId)
            .GetProperty("remaining").GetInt64();
    }

    // ---------- Trừ tồn ----------

    [DbFact]
    public async Task Tru_ton_theo_san_pham_da_chon()
    {
        var c = NewUser();
        var product = await NewProduct(c);

        var res = await Deduct(c, "SPXVN001", product, 2);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await Json.Read(res);
        Json.HasExactly(body, "orderCode", "saleGroupId", "unitsTotal", "products");
        Assert.Equal("SPXVN001", body.GetProperty("orderCode").GetString());
        Assert.Equal(2, body.GetProperty("unitsTotal").GetInt32());

        var line = body.GetProperty("products")[0];
        Json.HasExactly(line, "productId", "productName", "batchName", "quantity");
        Assert.Equal("Basic", line.GetProperty("productName").GetString());
        Assert.Equal("Lô A", line.GetProperty("batchName").GetString());

        Assert.Equal(98, await Remaining(c, product));
    }

    // Cái tên sai ở bản trước: nó tự đoán nhãn nói tới mặt hàng nào. Nay máy không chọn
    // gì cả — gửi id sản phẩm không có thật thì phải trả 400 chứ không âm thầm bỏ qua.
    [DbFact]
    public async Task San_pham_khong_ton_tai_thi_400()
    {
        var c = NewUser();
        await NewProduct(c);

        var res = await Deduct(c, "SPXVN002", Guid.NewGuid(), 1);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Sản phẩm không tồn tại hoặc không thuộc về bạn.",
            (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Khong_chon_dong_nao_thi_400()
    {
        var res = await NewUser().PostJson("/api/inventory/deduct-from-label",
            new { orderCode = "SPXVN003", lines = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Chưa chọn sản phẩm nào để trừ.",
            (await Json.Read(res)).GetProperty("error").GetString());
    }

    // Thiếu tồn thì KHÔNG trừ gì cả. Trừ được bao nhiêu hay bấy nhiêu rồi đánh dấu xong
    // là vừa giấu chuyện số liệu đã sai, vừa chặn luôn đường làm lại sau khi sửa.
    [DbFact]
    public async Task Thieu_ton_thi_khong_tru_gi_va_nhan_van_cho()
    {
        var c = NewUser();
        var product = await NewProduct(c, quantity: 3);

        var res = await Deduct(c, "SPXVN004", product, 5);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Không đủ tồn kho", (await Json.Read(res)).GetProperty("error").GetString());

        Assert.Equal(3, await Remaining(c, product));

        var actions = await Json.Read(await c.GetAsync("/api/inventory/label-actions"));
        Assert.Empty(actions.GetProperty("codes").EnumerateArray());
    }

    // Hai dòng cùng trỏ vào một sản phẩm phải CỘNG lại trước khi kiểm tồn, không thì mỗi
    // nửa tự nó qua được mà cộng lại thì vượt kho.
    [DbFact]
    public async Task Hai_dong_cung_san_pham_thi_cong_lai_truoc_khi_kiem()
    {
        var c = NewUser();
        var product = await NewProduct(c, quantity: 5);

        var res = await c.PostJson("/api/inventory/deduct-from-label", new
        {
            orderCode = "SPXVN005",
            lines = new[]
            {
                new { sourceName = Protect, variant = "", labelQty = 1, productId = product, quantity = 3 },
                new { sourceName = Combo2,  variant = "", labelQty = 1, productId = product, quantity = 3 },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(5, await Remaining(c, product));
    }

    // ---------- Chống làm hai lần ----------

    [DbFact]
    public async Task Tru_lai_nhan_da_tru_thi_400_va_khong_tru_them()
    {
        var c = NewUser();
        var product = await NewProduct(c);
        await Deduct(c, "SPXVN006", product, 2);

        var again = await Deduct(c, "SPXVN006", product, 2);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("đã trừ tồn rồi", (await Json.Read(again)).GetProperty("error").GetString());

        Assert.Equal(98, await Remaining(c, product));
    }

    [DbFact]
    public async Task Tru_mot_nhan_da_bo_qua_thi_400()
    {
        var c = NewUser();
        var product = await NewProduct(c);
        await Skip(c, "SPXVN007");

        var res = await Deduct(c, "SPXVN007", product, 1);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("đã đánh dấu bỏ qua", (await Json.Read(res)).GetProperty("error").GetString());
    }

    // ---------- Bỏ qua ----------

    [DbFact]
    public async Task Bo_qua_khong_sinh_dong_ban_nao()
    {
        var c = NewUser();
        var product = await NewProduct(c);

        var res = await Skip(c, "SPXVN008");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await Json.Read(res);
        Json.HasExactly(body, "skipped", "alreadyDecided");
        Assert.Equal(1, body.GetProperty("skipped").GetInt32());
        Assert.Empty(body.GetProperty("alreadyDecided").EnumerateArray());

        Assert.Equal(100, await Remaining(c, product));

        var actions = await Json.Read(await c.GetAsync("/api/inventory/label-actions"));
        var row = actions.GetProperty("recent")[0];
        Assert.Equal("skipped", row.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("saleGroupId").ValueKind);
        Assert.Equal(0, row.GetProperty("unitsTotal").GetInt32());
        Assert.True(row.GetProperty("canUndo").GetBoolean());
    }

    // Tick cả mẻ rồi bỏ qua một lượt là cách dùng bình thường — một request, không phải
    // hai trăm.
    [DbFact]
    public async Task Bo_qua_ca_me_trong_mot_luot()
    {
        var c = NewUser();

        var res = await Skip(c, "SPX-A", "SPX-B", "SPX-C");
        Assert.Equal(3, (await Json.Read(res)).GetProperty("skipped").GetInt32());

        var actions = await Json.Read(await c.GetAsync("/api/inventory/label-actions"));
        Assert.Equal(
            ["SPX-A", "SPX-B", "SPX-C"],
            actions.GetProperty("codes").EnumerateArray().Select(x => x.GetString()!).Order().ToArray());
    }

    // Màn hình lỡ cũ một phút thì một mã đã xử lý KHÔNG được làm hỏng 199 mã còn lại —
    // nhưng cũng không được lặng lẽ trôi qua.
    [DbFact]
    public async Task Ma_da_xu_ly_lan_trong_me_thi_bao_lai_chu_khong_do_ca_me()
    {
        var c = NewUser();
        var product = await NewProduct(c);
        await Deduct(c, "DA-TRU", product, 1);

        var body = await Json.Read(await Skip(c, "MOI-1", "DA-TRU", "MOI-2"));

        Assert.Equal(2, body.GetProperty("skipped").GetInt32());
        Assert.Equal(["DA-TRU"],
            body.GetProperty("alreadyDecided").EnumerateArray().Select(x => x.GetString()!).ToArray());

        // Và mã đã trừ vẫn là "đã trừ", không bị đổi thành bỏ qua.
        var actions = await Json.Read(await c.GetAsync("/api/inventory/label-actions"));
        var daTru = actions.GetProperty("recent").EnumerateArray()
            .First(x => x.GetProperty("orderCode").GetString() == "DA-TRU");
        Assert.Equal("deducted", daTru.GetProperty("status").GetString());
    }

    [DbFact]
    public async Task Bo_qua_ma_rong_thi_400()
    {
        var res = await NewUser().PostJson("/api/inventory/label-actions/skip",
            new { orderCodes = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Thiếu mã đơn.", (await Json.Read(res)).GetProperty("error").GetString());
    }


    // ---------- Danh sách ----------

    [DbFact]
    public async Task Danh_sach_tach_ma_da_xu_ly_khoi_lich_su_bay_ngay()
    {
        var c = NewUser();
        var product = await NewProduct(c);
        await Deduct(c, "SPXVN009", product, 1);
        await Skip(c, "SPXVN010");

        var body = await Json.Read(await c.GetAsync("/api/inventory/label-actions"));
        Json.HasExactly(body, "codes", "recent");

        var codes = body.GetProperty("codes").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(["SPXVN009", "SPXVN010"], codes.Order().ToArray());

        // Mới nhất trước.
        var recent = body.GetProperty("recent");
        Assert.Equal(2, recent.GetArrayLength());
        Assert.Equal("SPXVN010", recent[0].GetProperty("orderCode").GetString());
        Assert.True(recent[0].GetProperty("canUndo").GetBoolean());
    }

    // ---------- Hoàn lại ----------

    [DbFact]
    public async Task Hoan_lai_tra_ton_ve_va_nhan_quay_lai_danh_sach_cho()
    {
        var c = NewUser();
        var product = await NewProduct(c);
        await Deduct(c, "SPXVN011", product, 4);
        Assert.Equal(96, await Remaining(c, product));

        var res = await c.DeleteAsync("/api/inventory/label-actions/SPXVN011");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        Assert.Equal(100, await Remaining(c, product));

        var actions = await Json.Read(await c.GetAsync("/api/inventory/label-actions"));
        Assert.Empty(actions.GetProperty("codes").EnumerateArray());

        // Và trừ lại được, vì đó chính là lý do có nút hoàn lại.
        Assert.Equal(HttpStatusCode.OK, (await Deduct(c, "SPXVN011", product, 4)).StatusCode);
    }

    [DbFact]
    public async Task Hoan_lai_mot_lan_bo_qua()
    {
        var c = NewUser();
        await Skip(c, "SPXVN012");

        Assert.Equal(HttpStatusCode.NoContent,
            (await c.DeleteAsync("/api/inventory/label-actions/SPXVN012")).StatusCode);

        var actions = await Json.Read(await c.GetAsync("/api/inventory/label-actions"));
        Assert.Empty(actions.GetProperty("codes").EnumerateArray());
    }

    [DbFact]
    public async Task Hoan_lai_nhan_chua_xu_ly_thi_404()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await NewUser().DeleteAsync("/api/inventory/label-actions/KHONG-CO")).StatusCode);
    }

    // ---------- Gợi ý ----------

    // Nhớ lựa chọn để lần sau chọn sẵn — và chỉ để chọn sẵn. Nhãn thứ hai vẫn phải bấm.
    [DbFact]
    public async Task Tru_xong_thi_nho_chu_do_ung_voi_mat_hang_nao()
    {
        var c = NewUser();
        var product = await NewProduct(c);

        Assert.Empty((await Json.Read(await c.GetAsync("/api/inventory/label-aliases"))).EnumerateArray());

        await Deduct(c, "SPXVN013", product, 2, name: Combo2, labelQty: 1);

        var aliases = await Json.Read(await c.GetAsync("/api/inventory/label-aliases"));
        Assert.Equal(1, aliases.GetArrayLength());

        var a = aliases[0];
        Json.HasExactly(a, "id", "sourceName", "variant", "display", "kindId", "units");
        // Khoá tra cứu hạ chữ thường và bóp khoảng trắng; `display` giữ nguyên văn.
        Assert.Equal("protect basic (combo 2 cái )", a.GetProperty("sourceName").GetString());
        Assert.Equal("Protect Basic (Combo 2 cái )", a.GetProperty("display").GetString());
        // Một món trên nhãn hoá ra là 2 cái trong kho.
        Assert.Equal(2, a.GetProperty("units").GetInt32());
    }

    // Chọn lại khác đi thì gợi ý phải đi theo, không thì nó dạy sai mãi mãi.
    [DbFact]
    public async Task Chon_khac_di_thi_goi_y_ghi_de()
    {
        var c = NewUser();
        var basic = await NewProduct(c);
        var premium = await NewProduct(c, product: "Premium", batch: "Lô B", importDate: "2026-08-02");

        await Deduct(c, "SPXVN014", basic, 1, name: Protect);
        await Deduct(c, "SPXVN015", premium, 1, name: Protect);

        var aliases = await Json.Read(await c.GetAsync("/api/inventory/label-aliases"));
        Assert.Equal(1, aliases.GetArrayLength());

        var stock = await Json.Read(await c.GetAsync("/api/inventory/stock"));
        var premiumKind = stock.EnumerateArray()
            .First(x => x.GetProperty("id").GetGuid() == premium)
            .GetProperty("kindId").GetGuid();
        Assert.Equal(premiumKind, aliases[0].GetProperty("kindId").GetGuid());
    }

    // ---------- Xoá dòng bán phải gỡ dấu ----------

    // Trước đây xoá dòng bán xong là nhãn kẹt mãi: hàng đã về kho mà màn hình trừ tồn
    // vẫn coi như xong. Không có gì báo — đúng kiểu hỏng không dấu vết.
    [DbFact]
    public async Task Xoa_don_ban_thi_nhan_quay_lai_danh_sach_cho()
    {
        var c = NewUser();
        var product = await NewProduct(c);
        var groupId = (await Json.Read(await Deduct(c, "SPXVN016", product, 3)))
            .GetProperty("saleGroupId").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent,
            (await c.DeleteAsync($"/api/inventory/sales/group/{groupId}")).StatusCode);

        var actions = await Json.Read(await c.GetAsync("/api/inventory/label-actions"));
        Assert.Empty(actions.GetProperty("codes").EnumerateArray());
        Assert.Equal(100, await Remaining(c, product));
    }

    // ---------- Cách ly hai người dùng ----------

    [DbFact]
    public async Task Nguoi_khac_khong_thay_va_khong_hoan_lai_duoc_nhan_cua_minh()
    {
        var a = NewUser();
        var b = NewUser();
        var product = await NewProduct(a);
        await Deduct(a, "SPXVN017", product, 1);

        var mine = await Json.Read(await b.GetAsync("/api/inventory/label-actions"));
        Assert.Empty(mine.GetProperty("codes").EnumerateArray());
        Assert.Empty(mine.GetProperty("recent").EnumerateArray());
        Assert.Empty((await Json.Read(await b.GetAsync("/api/inventory/label-aliases"))).EnumerateArray());

        Assert.Equal(HttpStatusCode.NotFound,
            (await b.DeleteAsync("/api/inventory/label-actions/SPXVN017")).StatusCode);

        // Và cùng một mã đơn vẫn dùng được riêng cho từng người.
        var ofB = await NewProduct(b, batch: "Lô của B");
        Assert.Equal(HttpStatusCode.OK, (await Deduct(b, "SPXVN017", ofB, 1)).StatusCode);
    }

    [DbFact]
    public async Task Khong_tru_duoc_vao_san_pham_cua_nguoi_khac()
    {
        var a = NewUser();
        var b = NewUser();
        var ofA = await NewProduct(a);

        var res = await Deduct(b, "SPXVN018", ofA, 1);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(100, await Remaining(a, ofA));
    }

    // ---------- Một dòng nhãn, nhiều sản phẩm ----------

    // Nhãn in gộp một dòng "Protect × 5" mà trong gói thật ra là 3 Basic + 2 Premium.
    // Người đóng gói là người duy nhất biết, nên phải tách được.
    [DbFact]
    public async Task Tach_mot_dong_nhan_thanh_hai_san_pham()
    {
        var c = NewUser();
        var basic = await NewProduct(c, quantity: 10);
        var premium = await NewProduct(c, product: "Premium", quantity: 10,
            batch: "Lô B", importDate: "2026-08-02");

        var res = await c.PostJson("/api/inventory/deduct-from-label", new
        {
            orderCode = "SPXVN020",
            lines = new[]
            {
                new { sourceName = Protect, variant = "", labelQty = 5, productId = basic, quantity = 3 },
                new { sourceName = Protect, variant = "", labelQty = 5, productId = premium, quantity = 2 },
            },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json.Read(res);
        Assert.Equal(5, body.GetProperty("unitsTotal").GetInt32());
        Assert.Equal(2, body.GetProperty("products").GetArrayLength());

        Assert.Equal(7, await Remaining(c, basic));
        Assert.Equal(8, await Remaining(c, premium));
    }

    // Một chữ bị tách ra hai mặt hàng thì KHÔNG có gợi ý nào đúng cả. Nhớ lấy nửa nào
    // ghi sau cùng là lần sau điền sẵn một con số chưa bao giờ đúng.
    [DbFact]
    public async Task Dong_bi_tach_thi_khong_ghi_goi_y()
    {
        var c = NewUser();
        var basic = await NewProduct(c, quantity: 10);
        var premium = await NewProduct(c, product: "Premium", quantity: 10,
            batch: "Lô B", importDate: "2026-08-02");

        await c.PostJson("/api/inventory/deduct-from-label", new
        {
            orderCode = "SPXVN021",
            lines = new[]
            {
                new { sourceName = Protect, variant = "", labelQty = 5, productId = basic, quantity = 3 },
                new { sourceName = Protect, variant = "", labelQty = 5, productId = premium, quantity = 2 },
            },
        });

        Assert.Empty((await Json.Read(await c.GetAsync("/api/inventory/label-aliases"))).EnumerateArray());
    }

    // Còn dòng không bị tách thì vẫn nhớ như thường — cùng một request, hai số phận.
    [DbFact]
    public async Task Trong_cung_mot_nhan_dong_khong_tach_van_duoc_nho()
    {
        var c = NewUser();
        var basic = await NewProduct(c, quantity: 10);
        var premium = await NewProduct(c, product: "Premium", quantity: 10,
            batch: "Lô B", importDate: "2026-08-02");

        await c.PostJson("/api/inventory/deduct-from-label", new
        {
            orderCode = "SPXVN022",
            lines = new[]
            {
                new { sourceName = Protect, variant = "", labelQty = 5, productId = basic, quantity = 3 },
                new { sourceName = Protect, variant = "", labelQty = 5, productId = premium, quantity = 2 },
                new { sourceName = Combo2, variant = "", labelQty = 1, productId = basic, quantity = 2 },
            },
        });

        var aliases = await Json.Read(await c.GetAsync("/api/inventory/label-aliases"));
        Assert.Equal(1, aliases.GetArrayLength());
        Assert.Equal("protect basic (combo 2 cái )", aliases[0].GetProperty("sourceName").GetString());
        Assert.Equal(2, aliases[0].GetProperty("units").GetInt32());
    }
}
