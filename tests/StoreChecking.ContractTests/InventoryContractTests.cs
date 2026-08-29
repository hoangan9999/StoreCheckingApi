using System.Net;
using System.Text.Json;

namespace StoreChecking.ContractTests;

[Collection(nameof(ApiCollection))]
public sealed class InventoryContractTests(ApiFactory api)
{
    private HttpClient NewUser() => api.ClientFor(Guid.NewGuid());

    private const string SoldAt = "2026-08-15T10:00:00Z";

    /// <summary>Creates a batch with one product and hands back both ids — the usual starting point.</summary>
    private static async Task<(Guid Batch, Guid Product)> NewBatch(
        HttpClient c, int quantity = 10, decimal price = 100m, decimal cost = 500m, string name = "Lô A")
    {
        var created = await Json.Read(await c.PostJson("/api/inventory/batches", new
        {
            name,
            importDate = "2026-08-01",
            totalCost = cost,
            note = (string?)null,
            products = new[] { new { name = "SP1", quantity, sellPrice = price } },
        }));

        var batchId = created.GetProperty("id").GetGuid();
        var products = await Json.Read(await c.GetAsync($"/api/inventory/batches/{batchId}/products"));
        return (batchId, products[0].GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> Sell(
        HttpClient c, Guid productId, int qty, decimal price = 100m, decimal shipping = 0m) =>
        c.PostJson("/api/inventory/sales", new
        {
            items = new[] { new { productId, quantity = qty, sellPrice = price } },
            soldAt = SoldAt,
            shippingFee = shipping,
            note = (string?)null,
        });

    /// <summary>Places <paramref name="count"/> separate one-line orders, on consecutive days.</summary>
    private static async Task ManyOrders(HttpClient c, Guid productId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var res = await c.PostJson("/api/inventory/sales", new
            {
                items = new[] { new { productId, quantity = 1, sellPrice = 100m } },
                soldAt = $"2026-08-{i + 1:00}T10:00:00Z",
                shippingFee = 0m,
                note = (string?)null,
            });
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }
    }

    // ---------- Phân trang lịch sử bán ----------

    [DbFact]
    public async Task Mac_dinh_tra_20_don_nhung_dem_du_tong()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, quantity: 100);
        await ManyOrders(c, product, 25);

        var body = await Json.Read(await c.GetAsync("/api/inventory/sales"));
        Json.HasExactly(body, "total", "totalAmount", "limit", "offset", "items");

        // Tổng đếm theo CẢ khoảng, không phải theo số đã tải — nếu không thì "N đơn" trên
        // màn hình sẽ tụt xuống 20 và người dùng tưởng mất dữ liệu.
        Assert.Equal(25, body.GetProperty("total").GetInt32());
        Assert.Equal(2_500m, body.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(20, body.GetProperty("items").GetArrayLength());
    }

    [DbFact]
    public async Task Cuon_them_khong_lap_don_va_khong_sot_don()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, quantity: 100);
        await ManyOrders(c, product, 25);

        var first = await Json.Items(await c.GetAsync("/api/inventory/sales?limit=20&offset=0"));
        var second = await Json.Items(await c.GetAsync("/api/inventory/sales?limit=20&offset=20"));

        Assert.Equal(20, first.GetArrayLength());
        Assert.Equal(5, second.GetArrayLength());

        var ids = first.EnumerateArray().Concat(second.EnumerateArray())
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        Assert.Equal(25, ids.Count);
        Assert.Equal(25, ids.Distinct().Count());
    }

    // Paging counts orders, not rows. Were it counting rows, this two-line order would be
    // cut across the boundary and the page holding half of it would show half its total.
    [DbFact]
    public async Task Don_nhieu_dong_ve_nguyen_ven_khong_bi_cat_giua_hai_trang()
    {
        var c = NewUser();
        var created = await Json.Read(await c.PostJson("/api/inventory/batches", new
        {
            name = "Lô ghép",
            importDate = "2026-08-01",
            totalCost = 500m,
            note = (string?)null,
            products = new[]
            {
                new { name = "SP1", quantity = 10, sellPrice = 100m },
                new { name = "SP2", quantity = 10, sellPrice = 100m },
            },
        }));

        var batch = created.GetProperty("id").GetGuid();
        var products = await Json.Read(await c.GetAsync($"/api/inventory/batches/{batch}/products"));
        var p1 = products[0].GetProperty("id").GetGuid();
        var p2 = products[1].GetProperty("id").GetGuid();

        await c.PostJson("/api/inventory/sales", new
        {
            items = new[]
            {
                new { productId = p1, quantity = 1, sellPrice = 100m },
                new { productId = p2, quantity = 1, sellPrice = 100m },
            },
            soldAt = "2026-08-01T10:00:00Z", shippingFee = 0m, note = (string?)null,
        });
        await c.PostJson("/api/inventory/sales", new
        {
            items = new[] { new { productId = p1, quantity = 1, sellPrice = 100m } },
            soldAt = "2026-08-02T10:00:00Z", shippingFee = 0m, note = (string?)null,
        });

        var page1 = await Json.Read(await c.GetAsync("/api/inventory/sales?limit=1"));
        Assert.Equal(2, page1.GetProperty("total").GetInt32());
        Assert.Equal(1, page1.GetProperty("items").GetArrayLength());

        var page2 = await Json.Items(await c.GetAsync("/api/inventory/sales?limit=1&offset=1"));
        Assert.Equal(2, page2.GetArrayLength());

        var group = page2[0].GetProperty("saleGroupId").GetString();
        Assert.NotNull(group);
        Assert.Equal(group, page2[1].GetProperty("saleGroupId").GetString());
    }

    [DbFact]
    public async Task Tong_tien_tru_phi_ship_va_tinh_tren_ca_khoang()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, quantity: 100);

        await Sell(c, product, 2, shipping: 30m);      // 2 x 100 - 30 = 170
        await Sell(c, product, 1);                     // 100

        var body = await Json.Read(await c.GetAsync("/api/inventory/sales?limit=1"));

        Assert.Equal(1, body.GetProperty("items").GetArrayLength());
        Assert.Equal(270m, body.GetProperty("totalAmount").GetDecimal());
    }

    // `to` is exclusive, which is what makes "chỉ hôm nay" mean one day and not two.
    [DbFact]
    public async Task Loc_theo_khoang_thoi_gian_khong_tinh_moc_cuoi()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, quantity: 100);
        await ManyOrders(c, product, 5);               // ngày 01..05 tháng 8

        var body = await Json.Read(await c.GetAsync(
            "/api/inventory/sales?from=2026-08-03T00:00:00Z&to=2026-08-05T00:00:00Z"));

        Assert.Equal(2, body.GetProperty("total").GetInt32());       // ngày 03 và 04
        Assert.Equal(200m, body.GetProperty("totalAmount").GetDecimal());
    }

    // ---------- Lô hàng ----------

    [DbFact]
    public async Task Tao_lo_kem_san_pham_trong_mot_lan()
    {
        var c = NewUser();

        var res = await c.PostJson("/api/inventory/batches", new
        {
            name = "  Lô tháng 8  ",
            importDate = "2026-08-01",
            totalCost = 1_000_000m,
            note = "  hàng Nhật  ",
            products = new[]
            {
                new { name = "Áo", quantity = 10, sellPrice = 150_000m },
                new { name = "Quần", quantity = 5, sellPrice = 250_000m },
            },
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var b = await Json.Read(res);
        Json.HasExactly(b, "id", "name", "importDate", "totalCost", "note", "priority", "createdAt",
            "productCount", "totalQty", "soldQty", "damagedQty", "remainingQty", "revenue", "profit");

        Assert.Equal("Lô tháng 8", b.GetProperty("name").GetString());
        Assert.Equal("2026-08-01", b.GetProperty("importDate").GetString());
        Assert.Equal("hàng Nhật", b.GetProperty("note").GetString());
        Assert.Equal(2, b.GetProperty("productCount").GetInt64());
        Assert.Equal(15, b.GetProperty("totalQty").GetInt64());
        Assert.Equal(15, b.GetProperty("remainingQty").GetInt64());
        Assert.Equal(0m, b.GetProperty("revenue").GetDecimal());
        Assert.Equal(-1_000_000m, b.GetProperty("profit").GetDecimal());   // chưa bán gì thì lỗ đúng bằng vốn
    }

    [DbFact]
    public async Task Tao_lo_khong_kem_san_pham_van_duoc()
    {
        var c = NewUser();
        var b = await Json.Read(await c.PostJson("/api/inventory/batches", new
        {
            name = "Lô rỗng", importDate = "2026-08-01", totalCost = 0m,
            note = (string?)null, products = Array.Empty<object>(),
        }));

        Assert.Equal(0, b.GetProperty("productCount").GetInt64());
        Assert.Equal(0, (await Json.Read(await c.GetAsync($"/api/inventory/batches/{b.GetProperty("id").GetGuid()}/products"))).GetArrayLength());
    }

    [DbFact]
    public async Task Thieu_ten_lo_hoac_ngay_sai_thi_400()
    {
        var c = NewUser();

        var noName = await c.PostJson("/api/inventory/batches",
            new { name = "  ", importDate = "2026-08-01", totalCost = 0m, note = (string?)null, products = Array.Empty<object>() });
        Assert.Equal("Thiếu tên lô.", (await Json.Read(noName)).GetProperty("error").GetString());

        var badDate = await c.PostJson("/api/inventory/batches",
            new { name = "Lô", importDate = "01/08/2026", totalCost = 0m, note = (string?)null, products = Array.Empty<object>() });
        Assert.Equal("Ngày nhập phải dạng YYYY-MM-DD.", (await Json.Read(badDate)).GetProperty("error").GetString());
    }

    // The batch_summary view has no priority column, so the API reads it from the table and
    // merges. Before, the client fetched both and merged in the browser.
    [DbFact]
    public async Task Danh_sach_lo_sap_theo_uu_tien_roi_moi_nhat_truoc()
    {
        var c = NewUser();
        var (first, _) = await NewBatch(c, name: "Không ưu tiên");
        var (second, _) = await NewBatch(c, name: "Ưu tiên 1");
        var (third, _) = await NewBatch(c, name: "Ưu tiên 2");

        await c.PutJson($"/api/inventory/batches/{second}", new
        { name = "Ưu tiên 1", importDate = "2026-08-01", totalCost = 500m, note = (string?)null, priority = 1 });
        await c.PutJson($"/api/inventory/batches/{third}", new
        { name = "Ưu tiên 2", importDate = "2026-08-01", totalCost = 500m, note = (string?)null, priority = 2 });

        var listed = await Json.Read(await c.GetAsync("/api/inventory/batches"));
        Assert.Equal(3, listed.GetArrayLength());

        // Ranked first, in order; the unranked one falls to the end.
        Assert.Equal(second, listed[0].GetProperty("id").GetGuid());
        Assert.Equal(third, listed[1].GetProperty("id").GetGuid());
        Assert.Equal(first, listed[2].GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, listed[2].GetProperty("priority").ValueKind);
    }

    [DbFact]
    public async Task Dat_lai_uu_tien_nhieu_lo_cung_luc()
    {
        var c = NewUser();
        var (a, _) = await NewBatch(c, name: "A");
        var (b, _) = await NewBatch(c, name: "B");

        var res = await c.PutJson("/api/inventory/batches/priorities", new
        {
            items = new[] { new { id = b, priority = 1 }, new { id = a, priority = 2 } },
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, (await Json.Read(res)).GetProperty("changed").GetInt32());

        var listed = await Json.Read(await c.GetAsync("/api/inventory/batches"));
        Assert.Equal(b, listed[0].GetProperty("id").GetGuid());
    }

    [DbFact]
    public async Task Xoa_lo_keo_theo_san_pham_va_lich_su_ban()
    {
        var c = NewUser();
        var (batch, product) = await NewBatch(c);
        await Sell(c, product, 2);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/inventory/batches/{batch}")).StatusCode);

        Assert.Equal(0, (await Json.Read(await c.GetAsync("/api/inventory/batches"))).GetArrayLength());
        Assert.Equal(0, (await Json.Items(await c.GetAsync("/api/inventory/sales"))).GetArrayLength());
        Assert.Equal(0, (await Json.Read(await c.GetAsync("/api/inventory/stock"))).GetArrayLength());
    }

    // ---------- Bán hàng: số liệu của hai view ----------

    // Revenue subtracts shipping and remaining subtracts damaged stock. Both were missing
    // from the first port of the views and only found when copying the sales data failed.
    [DbFact]
    public async Task Doanh_thu_tru_phi_ship_va_ton_tru_hang_hu()
    {
        var c = NewUser();
        var (batch, product) = await NewBatch(c, quantity: 10, price: 100m, cost: 500m);

        await Sell(c, product, 3, price: 100m, shipping: 50m);      // 300 - 50 = 250
        await c.PostJson("/api/inventory/damages", new { productId = product, quantity = 2, note = "vỡ" });

        var b = await Json.Read(await c.GetAsync($"/api/inventory/batches/{batch}"));
        Assert.Equal(3, b.GetProperty("soldQty").GetInt64());
        Assert.Equal(2, b.GetProperty("damagedQty").GetInt64());
        Assert.Equal(5, b.GetProperty("remainingQty").GetInt64());   // 10 - 3 - 2
        Assert.Equal(250m, b.GetProperty("revenue").GetDecimal());   // phí ship đã trừ
        Assert.Equal(-250m, b.GetProperty("profit").GetDecimal());   // 250 - 500

        var products = await Json.Read(await c.GetAsync($"/api/inventory/batches/{batch}/products"));
        var p = products[0];
        Json.HasExactly(p, "id", "batchId", "name", "quantity", "sellPrice", "createdAt",
            "soldQty", "damagedQty", "remaining", "revenue");
        Assert.Equal(5, p.GetProperty("remaining").GetInt64());
        Assert.Equal(250m, p.GetProperty("revenue").GetDecimal());
    }

    [DbFact]
    public async Task Ban_mot_dong_thi_khong_co_sale_group()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c);

        var rows = await Json.Read(await Sell(c, product, 2));
        Assert.Equal(1, rows.GetArrayLength());

        var s = rows[0];
        Json.HasExactly(s, "id", "productId", "batchId", "quantity", "sellPrice", "shippingFee",
            "note", "saleGroupId", "soldAt");
        Assert.Equal(JsonValueKind.Null, s.GetProperty("saleGroupId").ValueKind);
    }

    // Shipping belongs to the ORDER. Repeating it on every line would count postage once
    // per line and make revenue come out short.
    [DbFact]
    public async Task Don_nhieu_dong_chung_group_va_phi_ship_chi_o_dong_dau()
    {
        var c = NewUser();
        var (_, p1) = await NewBatch(c, name: "Lô 1");
        var (_, p2) = await NewBatch(c, name: "Lô 2");

        var rows = await Json.Read(await c.PostJson("/api/inventory/sales", new
        {
            items = new[]
            {
                new { productId = p1, quantity = 1, sellPrice = 100m },
                new { productId = p2, quantity = 2, sellPrice = 200m },
            },
            soldAt = SoldAt,
            shippingFee = 30m,
            note = "đơn gộp",
        }));

        Assert.Equal(2, rows.GetArrayLength());

        var group = rows[0].GetProperty("saleGroupId").GetGuid();
        Assert.Equal(group, rows[1].GetProperty("saleGroupId").GetGuid());

        Assert.Equal(30m, rows[0].GetProperty("shippingFee").GetDecimal());
        Assert.Equal(0m, rows[1].GetProperty("shippingFee").GetDecimal());
        Assert.Equal("đơn gộp", rows[0].GetProperty("note").GetString());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("note").ValueKind);
    }

    [DbFact]
    public async Task Xoa_ca_mot_don()
    {
        var c = NewUser();
        var (_, p1) = await NewBatch(c, name: "Lô 1");
        var (_, p2) = await NewBatch(c, name: "Lô 2");

        var rows = await Json.Read(await c.PostJson("/api/inventory/sales", new
        {
            items = new[]
            {
                new { productId = p1, quantity = 1, sellPrice = 100m },
                new { productId = p2, quantity = 1, sellPrice = 100m },
            },
            soldAt = SoldAt, shippingFee = 0m, note = (string?)null,
        }));
        var group = rows[0].GetProperty("saleGroupId").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/inventory/sales/group/{group}")).StatusCode);
        Assert.Equal(0, (await Json.Items(await c.GetAsync("/api/inventory/sales"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/inventory/sales/group/{Guid.NewGuid()}")).StatusCode);
    }

    [DbFact]
    public async Task Lich_su_ban_kem_ten_san_pham_va_ten_lo()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, name: "Lô đặt tên");
        await Sell(c, product, 1);

        var rows = await Json.Items(await c.GetAsync("/api/inventory/sales"));
        Assert.Equal(1, rows.GetArrayLength());

        var s = rows[0];
        Json.HasExactly(s, "id", "productId", "batchId", "quantity", "sellPrice", "shippingFee",
            "note", "saleGroupId", "soldAt", "productName", "batchName", "batchPriority");
        Assert.Equal("SP1", s.GetProperty("productName").GetString());
        Assert.Equal("Lô đặt tên", s.GetProperty("batchName").GetString());
    }

    // ---------- Chặn bán vượt tồn ----------

    [DbFact]
    public async Task Ban_vuot_ton_thi_400_kem_so_con_lai()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, quantity: 5);

        var res = await Sell(c, product, 6);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Không đủ tồn kho cho 'SP1': còn 5, yêu cầu bán 6.",
            (await Json.Read(res)).GetProperty("error").GetString());

        Assert.Equal(0, (await Json.Items(await c.GetAsync("/api/inventory/sales"))).GetArrayLength());
    }

    [DbFact]
    public async Task Hang_hu_cung_tru_vao_ton_kha_dung_khi_ban()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, quantity: 5);
        await c.PostJson("/api/inventory/damages", new { productId = product, quantity = 3, note = (string?)null });

        var res = await Sell(c, product, 3);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Không đủ tồn kho cho 'SP1': còn 2, yêu cầu bán 3.",
            (await Json.Read(res)).GetProperty("error").GetString());
    }

    // Two lines of the same product in one order have to fit together, not each on its own.
    [DbFact]
    public async Task Hai_dong_cung_san_pham_trong_mot_don_tinh_gop()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, quantity: 5);

        var res = await c.PostJson("/api/inventory/sales", new
        {
            items = new[]
            {
                new { productId = product, quantity = 3, sellPrice = 100m },
                new { productId = product, quantity = 3, sellPrice = 100m },
            },
            soldAt = SoldAt, shippingFee = 0m, note = (string?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Không đủ tồn kho cho 'SP1': còn 2, yêu cầu bán 3.",
            (await Json.Read(res)).GetProperty("error").GetString());
        Assert.Equal(0, (await Json.Items(await c.GetAsync("/api/inventory/sales"))).GetArrayLength());
    }

    [DbFact]
    public async Task Ghi_hang_hu_vuot_ton_thi_400()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, quantity: 4);
        await Sell(c, product, 3);

        var res = await c.PostJson("/api/inventory/damages", new { productId = product, quantity = 2, note = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Không đủ tồn để ghi hư: còn 1, yêu cầu 2.",
            (await Json.Read(res)).GetProperty("error").GetString());
    }

    // Editing a sale of 3 down to 2 must not be measured against stock that still counts
    // those 3 as gone — the line being edited is left out of the sum.
    [DbFact]
    public async Task Sua_lan_ban_khong_tinh_chinh_no_vao_ton_da_dung()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, quantity: 5);
        var sale = (await Json.Read(await Sell(c, product, 5))).EnumerateArray().First().GetProperty("id").GetGuid();

        // Down to 4: fine, even though 5 are currently recorded as sold.
        var down = await c.PutJson($"/api/inventory/sales/{sale}", new
        { quantity = 4, sellPrice = 120m, soldAt = SoldAt, shippingFee = 10m, note = "sửa" });
        Assert.Equal(HttpStatusCode.OK, down.StatusCode);
        Assert.Equal(4, (await Json.Read(down)).GetProperty("quantity").GetInt32());

        // Up to 6: still refused, because only 5 ever came in.
        var up = await c.PutJson($"/api/inventory/sales/{sale}", new
        { quantity = 6, sellPrice = 120m, soldAt = SoldAt, shippingFee = 0m, note = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, up.StatusCode);
        Assert.Equal("Không đủ tồn kho: còn 5, yêu cầu bán 6.", (await Json.Read(up)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task So_luong_ban_phai_lon_hon_0()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c);

        Assert.Equal("Số lượng bán phải lớn hơn 0.",
            (await Json.Read(await Sell(c, product, 0))).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Don_khong_co_san_pham_thi_400()
    {
        var res = await NewUser().PostJson("/api/inventory/sales", new
        { items = Array.Empty<object>(), soldAt = SoldAt, shippingFee = 0m, note = (string?)null });

        Assert.Equal("Đơn hàng không có sản phẩm nào.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    // ---------- Sản phẩm ----------

    [DbFact]
    public async Task Khong_ha_duoc_so_luong_nhap_xuong_duoi_so_da_ban()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, quantity: 10);
        await Sell(c, product, 6);
        await c.PostJson("/api/inventory/damages", new { productId = product, quantity = 1, note = (string?)null });

        var res = await c.PutJson($"/api/inventory/products/{product}",
            new { name = "SP1", quantity = 5, sellPrice = 100m });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Đã bán hoặc hư 7, không thể giảm số lượng nhập xuống 5.",
            (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Them_va_sua_san_pham()
    {
        var c = NewUser();
        var (batch, _) = await NewBatch(c);

        var added = await c.PostJson($"/api/inventory/batches/{batch}/products",
            new { name = "  SP2  ", quantity = 3, sellPrice = 70m });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var p = await Json.Read(added);
        Assert.Equal("SP2", p.GetProperty("name").GetString());
        Assert.Equal(3, p.GetProperty("remaining").GetInt64());

        var updated = await Json.Read(await c.PutJson($"/api/inventory/products/{p.GetProperty("id").GetGuid()}",
            new { name = "SP2 đổi tên", quantity = 8, sellPrice = 90m }));
        Assert.Equal("SP2 đổi tên", updated.GetProperty("name").GetString());
        Assert.Equal(8, updated.GetProperty("remaining").GetInt64());
    }

    [DbFact]
    public async Task Chi_liet_ke_san_pham_con_hang()
    {
        var c = NewUser();
        var (_, product) = await NewBatch(c, quantity: 2, name: "Lô ưu tiên");

        Assert.Equal(1, (await Json.Read(await c.GetAsync("/api/inventory/stock"))).GetArrayLength());

        await Sell(c, product, 2);   // bán hết

        Assert.Equal(0, (await Json.Read(await c.GetAsync("/api/inventory/stock"))).GetArrayLength());
    }

    [DbFact]
    public async Task Ton_kho_kem_ten_lo_va_sap_theo_uu_tien_lo()
    {
        var c = NewUser();
        var (low, _) = await NewBatch(c, name: "Lô sau");
        var (high, _) = await NewBatch(c, name: "Lô trước");

        await c.PutJson($"/api/inventory/batches/{high}", new
        { name = "Lô trước", importDate = "2026-08-01", totalCost = 500m, note = (string?)null, priority = 1 });
        await c.PutJson($"/api/inventory/batches/{low}", new
        { name = "Lô sau", importDate = "2026-08-01", totalCost = 500m, note = (string?)null, priority = 5 });

        var stock = await Json.Read(await c.GetAsync("/api/inventory/stock"));
        Json.HasExactly(stock[0], "id", "name", "batchId", "batchName", "batchPriority", "sellPrice", "remaining");
        Assert.Equal("Lô trước", stock[0].GetProperty("batchName").GetString());
        Assert.Equal(1, stock[0].GetProperty("batchPriority").GetInt32());
        Assert.Equal("Lô sau", stock[1].GetProperty("batchName").GetString());
    }

    // ---------- 404 ----------

    [DbFact]
    public async Task Thao_tac_tren_thu_khong_ton_tai_deu_404()
    {
        var c = NewUser();
        var missing = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/inventory/batches/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/inventory/batches/{missing}/products")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/inventory/batches/{missing}/sales")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/inventory/batches/{missing}/damages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/inventory/batches/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/inventory/products/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/inventory/sales/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/inventory/damages/{missing}")).StatusCode);
    }

    [DbFact]
    public async Task Khong_co_token_thi_401()
    {
        var anon = api.AnonymousClient();

        foreach (var url in new[] { "/api/inventory/batches", "/api/inventory/stock", "/api/inventory/sales" })
            Assert.True((await anon.GetAsync(url)).StatusCode == HttpStatusCode.Unauthorized,
                $"{url} phải trả 401 khi không có token.");
    }
}
