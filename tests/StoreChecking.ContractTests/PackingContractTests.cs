using System.Net;
using System.Text.Json;

namespace StoreChecking.ContractTests;

[Collection(nameof(ApiCollection))]
public sealed class PackingContractTests(ApiFactory api)
{
    private HttpClient NewUser() => api.ClientFor(Guid.NewGuid());

    [DbFact]
    public async Task Chua_quay_gi_thi_tra_mang_rong()
    {
        var res = await NewUser().GetAsync("/api/packing");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(0, (await Json.Read(res)).GetArrayLength());
    }

    [DbFact]
    public async Task Ghi_mot_lan_quay_roi_doc_lai()
    {
        var c = NewUser();

        var saved = await c.PostJson("/api/packing", new { orderCode = "  SPX12345  ", ext = "mp4" });
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);

        var s = await Json.Read(saved);
        Json.HasExactly(s, "seq", "filename");
        Assert.Equal(1, s.GetProperty("seq").GetInt32());
        Assert.Equal("SPX12345_1.mp4", s.GetProperty("filename").GetString());   // mã đơn đã trim

        var listed = await Json.Read(await c.GetAsync("/api/packing"));
        Assert.Equal(1, listed.GetArrayLength());

        var row = listed[0];
        Json.HasExactly(row, "id", "orderCode", "seq", "note", "filename", "recordedAt");
        Assert.Equal("SPX12345", row.GetProperty("orderCode").GetString());
        Assert.Equal("SPX12345_1.mp4", row.GetProperty("filename").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("note").ValueKind);
    }

    [DbFact]
    public async Task Quay_lai_cung_mot_don_thi_seq_tang_dan()
    {
        var c = NewUser();

        for (var i = 1; i <= 3; i++)
        {
            var s = await Json.Read(await c.PostJson("/api/packing", new { orderCode = "DON-A", ext = "mp4" }));
            Assert.Equal(i, s.GetProperty("seq").GetInt32());
            Assert.Equal($"DON-A_{i}.mp4", s.GetProperty("filename").GetString());
        }
    }

    // The Supabase version counted rows to pick the next seq. Deleting the middle recording
    // dropped the count, so the next upload was handed a name that already existed on the
    // NAS and would overwrite a video still referenced by another row. Derived from the
    // highest seq instead, which cannot go backwards.
    [DbFact]
    public async Task Xoa_mot_lan_quay_giua_chung_thi_seq_khong_lui_lai()
    {
        var c = NewUser();
        for (var i = 0; i < 3; i++) await c.PostJson("/api/packing", new { orderCode = "DON-B", ext = "mp4" });

        var listed = await Json.Read(await c.GetAsync("/api/packing?search=DON-B"));
        var middle = listed.EnumerateArray().First(x => x.GetProperty("seq").GetInt32() == 2);
        Assert.Equal(HttpStatusCode.NoContent,
            (await c.DeleteAsync($"/api/packing/{middle.GetProperty("id").GetGuid()}")).StatusCode);

        var next = await Json.Read(await c.PostJson("/api/packing", new { orderCode = "DON-B", ext = "mp4" }));
        Assert.Equal(4, next.GetProperty("seq").GetInt32());
        Assert.Equal("DON-B_4.mp4", next.GetProperty("filename").GetString());
    }

    [DbFact]
    public async Task Thieu_ma_don_thi_400()
    {
        var res = await NewUser().PostJson("/api/packing", new { orderCode = "   ", ext = "mp4" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Thiếu mã đơn.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Khong_dua_duoi_file_thi_mac_dinh_mp4()
    {
        var c = NewUser();
        var s = await Json.Read(await c.PostJson("/api/packing", new { orderCode = "DON-C", ext = (string?)null }));

        Assert.Equal("DON-C_1.mp4", s.GetProperty("filename").GetString());
    }

    [DbFact]
    public async Task Duoi_file_co_dau_cham_dau_van_nhan()
    {
        var c = NewUser();
        var s = await Json.Read(await c.PostJson("/api/packing", new { orderCode = "DON-D", ext = ".webm" }));

        Assert.Equal("DON-D_1.webm", s.GetProperty("filename").GetString());
    }

    // The name goes straight into a path on the NAS, so anything that is not a plain
    // extension is refused rather than sanitised quietly.
    [DbFact]
    public async Task Duoi_file_bay_ba_thi_400()
    {
        var res = await NewUser().PostJson("/api/packing", new { orderCode = "DON-E", ext = "../../etc" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Đuôi file không hợp lệ.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Tim_theo_ma_don_khong_phan_biet_hoa_thuong()
    {
        var c = NewUser();
        await c.PostJson("/api/packing", new { orderCode = "SPX-AAA", ext = "mp4" });
        await c.PostJson("/api/packing", new { orderCode = "GHN-BBB", ext = "mp4" });

        Assert.Equal(1, (await Json.Read(await c.GetAsync("/api/packing?search=spx"))).GetArrayLength());
        Assert.Equal(1, (await Json.Read(await c.GetAsync("/api/packing?search=ghn"))).GetArrayLength());
        Assert.Equal(2, (await Json.Read(await c.GetAsync("/api/packing?search=%20%20"))).GetArrayLength());
    }

    [DbFact]
    public async Task Limit_mac_dinh_100_va_nhan_toi_10000()
    {
        var c = NewUser();
        for (var i = 0; i < 3; i++) await c.PostJson("/api/packing", new { orderCode = $"DON-L{i}", ext = "mp4" });

        // The sync and cleanup screens ask for everything at once; a silent truncation
        // there would make logged files look unlogged and import them twice.
        Assert.Equal(3, (await Json.Read(await c.GetAsync("/api/packing?limit=10000"))).GetArrayLength());
        Assert.Equal(1, (await Json.Read(await c.GetAsync("/api/packing?limit=1"))).GetArrayLength());
        Assert.Equal(3, (await Json.Read(await c.GetAsync("/api/packing?limit=0"))).GetArrayLength());
    }

    [DbFact]
    public async Task Danh_sach_ten_file()
    {
        var c = NewUser();
        await c.PostJson("/api/packing", new { orderCode = "DON-F", ext = "mp4" });
        await c.PostJson("/api/packing", new { orderCode = "DON-G", ext = "mp4" });

        var names = await Json.Read(await c.GetAsync("/api/packing/filenames"));
        var all = names.EnumerateArray().Select(x => x.GetString()).ToList();

        Assert.Equal(2, all.Count);
        Assert.Contains("DON-F_1.mp4", all);
        Assert.Contains("DON-G_1.mp4", all);
    }

    [DbFact]
    public async Task Nhap_hang_loat_tu_NAS()
    {
        var c = NewUser();

        var res = await c.PostJson("/api/packing/import", new
        {
            items = new[]
            {
                new { orderCode = "IMP-1", seq = 1, filename = "IMP-1_1.mp4", recordedAt = "2026-08-01T10:00:00Z" },
                new { orderCode = "IMP-2", seq = 1, filename = "IMP-2_1.mp4", recordedAt = "2026-08-02T10:00:00Z" },
            },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var r = await Json.Read(res);
        Json.HasExactly(r, "added", "skipped");
        Assert.Equal(2, r.GetProperty("added").GetInt32());
        Assert.Equal(0, r.GetProperty("skipped").GetInt32());

        var listed = await Json.Read(await c.GetAsync("/api/packing"));
        Assert.Equal(2, listed.GetArrayLength());
        // Newest first, by the recorded time carried over from the NAS file.
        Assert.Equal("IMP-2", listed[0].GetProperty("orderCode").GetString());
    }

    // A retry after a half-finished import must not log the same file twice; there is no
    // unique index on filename to catch it.
    [DbFact]
    public async Task Nhap_lai_ten_file_da_co_thi_bo_qua()
    {
        var c = NewUser();
        var body = new
        {
            items = new[]
            {
                new { orderCode = "IMP-X", seq = 1, filename = "IMP-X_1.mp4", recordedAt = "2026-08-01T10:00:00Z" },
            },
        };

        Assert.Equal(1, (await Json.Read(await c.PostJson("/api/packing/import", body))).GetProperty("added").GetInt32());

        var again = await Json.Read(await c.PostJson("/api/packing/import", body));
        Assert.Equal(0, again.GetProperty("added").GetInt32());
        Assert.Equal(1, again.GetProperty("skipped").GetInt32());

        Assert.Equal(1, (await Json.Read(await c.GetAsync("/api/packing"))).GetArrayLength());
    }

    [DbFact]
    public async Task Nhap_trung_ten_file_trong_cung_mot_lan_goi_cung_bi_chan()
    {
        var c = NewUser();
        var r = await Json.Read(await c.PostJson("/api/packing/import", new
        {
            items = new[]
            {
                new { orderCode = "IMP-Y", seq = 1, filename = "IMP-Y_1.mp4", recordedAt = "2026-08-01T10:00:00Z" },
                new { orderCode = "IMP-Y", seq = 1, filename = "IMP-Y_1.mp4", recordedAt = "2026-08-01T10:00:00Z" },
            },
        }));

        Assert.Equal(1, r.GetProperty("added").GetInt32());
        Assert.Equal(1, r.GetProperty("skipped").GetInt32());
    }

    [DbFact]
    public async Task Xoa_dong_khong_ton_tai_thi_404()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await NewUser().DeleteAsync($"/api/packing/{Guid.NewGuid()}")).StatusCode);
    }

    [DbFact]
    public async Task Khong_co_token_thi_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.AnonymousClient().GetAsync("/api/packing")).StatusCode);
    }
}
