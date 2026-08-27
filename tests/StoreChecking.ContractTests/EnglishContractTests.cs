using System.Net;
using System.Text.Json;

namespace StoreChecking.ContractTests;

[Collection(nameof(ApiCollection))]
public sealed class EnglishContractTests(ApiFactory api)
{
    private HttpClient NewUser() => api.ClientFor(Guid.NewGuid());

    private static object SampleData(string meaning) => new
    {
        meaning,
        examples = new[] { new { tense = "present", text = "I go to work." } },
    };

    // ---------- Saved vocabulary ----------

    [DbFact]
    public async Task Danh_sach_tu_rong_van_tra_du_bon_truong()
    {
        var res = await NewUser().GetAsync("/api/english/words");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json.Read(res);

        Json.HasExactly(body, "total", "limit", "offset", "items");
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Equal(50, body.GetProperty("limit").GetInt32());     // default page size
        Assert.Equal(0, body.GetProperty("offset").GetInt32());
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
    }

    // GET /words returned a bare 500 for its entire life because the jsonb column was
    // touched inside the LINQ projection. This test is the reason that cannot come back.
    [DbFact]
    public async Task Luu_tu_roi_doc_lai_giu_nguyen_jsonb()
    {
        var c = NewUser();

        var created = await c.PostJson("/api/english/words",
            new { word = "  commute  ", data = SampleData("di lam hang ngay") });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var word = await Json.Read(created);
        Json.HasExactly(word, "id", "word", "data", "createdAt");
        Assert.Equal("commute", word.GetProperty("word").GetString());   // trimmed on write
        Assert.Equal("di lam hang ngay", word.GetProperty("data").GetProperty("meaning").GetString());

        var listed = await Json.Read(await c.GetAsync("/api/english/words"));
        Assert.Equal(1, listed.GetProperty("total").GetInt32());

        var item = listed.GetProperty("items")[0];
        Json.HasExactly(item, "id", "word", "data", "createdAt");
        Assert.Equal(JsonValueKind.Object, item.GetProperty("data").ValueKind);
        Assert.Equal("present", item.GetProperty("data").GetProperty("examples")[0].GetProperty("tense").GetString());
    }

    [DbFact]
    public async Task Tu_rong_thi_400()
    {
        var res = await NewUser().PostJson("/api/english/words", new { word = "   ", data = SampleData("x") });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Thiếu từ vựng.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Tu_moi_nhat_dung_dau_va_phan_trang_khong_lap_dong()
    {
        var c = NewUser();
        for (var i = 1; i <= 5; i++)
            await c.PostJson("/api/english/words", new { word = $"tu-{i}", data = SampleData($"nghia {i}") });

        // Rows written in the same transaction share created_at, so ordering must fall
        // back to Id. Without that tie-break page 2 repeats rows already shown on page 1.
        var p1 = await Json.Read(await c.GetAsync("/api/english/words?limit=2&offset=0"));
        var p2 = await Json.Read(await c.GetAsync("/api/english/words?limit=2&offset=2"));
        var p3 = await Json.Read(await c.GetAsync("/api/english/words?limit=2&offset=4"));

        Assert.Equal(5, p1.GetProperty("total").GetInt32());
        Assert.Equal(2, p1.GetProperty("limit").GetInt32());
        Assert.Equal(2, p2.GetProperty("offset").GetInt32());

        var ids = new[] { p1, p2, p3 }
            .SelectMany(p => p.GetProperty("items").EnumerateArray())
            .Select(x => x.GetProperty("id").GetGuid())
            .ToList();

        Assert.Equal(5, ids.Count);
        Assert.Equal(5, ids.Distinct().Count());
    }

    [DbFact]
    public async Task Limit_bi_chan_tren_200_va_duoi_1()
    {
        var c = NewUser();

        Assert.Equal(200, (await Json.Read(await c.GetAsync("/api/english/words?limit=9999"))).GetProperty("limit").GetInt32());
        Assert.Equal(50, (await Json.Read(await c.GetAsync("/api/english/words?limit=0"))).GetProperty("limit").GetInt32());
        Assert.Equal(50, (await Json.Read(await c.GetAsync("/api/english/words?limit=-5"))).GetProperty("limit").GetInt32());
        Assert.Equal(0, (await Json.Read(await c.GetAsync("/api/english/words?offset=-5"))).GetProperty("offset").GetInt32());
    }

    [DbFact]
    public async Task Xoa_tu_khong_ton_tai_thi_404()
    {
        var c = NewUser();
        var created = await Json.Read(await c.PostJson("/api/english/words", new { word = "delete-me", data = SampleData("x") }));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/english/words/{created.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/english/words/{Guid.NewGuid()}")).StatusCode);
    }

    // ---------- Sentences kept from speaking practice ----------

    [DbFact]
    public async Task Luu_cau_roi_doc_lai()
    {
        var c = NewUser();

        var created = await c.PostJson("/api/english/sentences",
            new { text = "  I used to walk there.  ", note = " cach noi tu nhien hon " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var s = await Json.Read(created);
        Json.HasExactly(s, "id", "text", "note", "createdAt");
        Assert.Equal("I used to walk there.", s.GetProperty("text").GetString());
        Assert.Equal("cach noi tu nhien hon", s.GetProperty("note").GetString());

        var listed = await Json.Read(await c.GetAsync("/api/english/sentences"));
        Json.HasExactly(listed, "total", "limit", "offset", "items");
        Assert.Equal(1, listed.GetProperty("total").GetInt32());
    }

    [DbFact]
    public async Task Luu_khong_co_note_thi_note_la_chuoi_rong_chu_khong_null()
    {
        var c = NewUser();
        var s = await Json.Read(await c.PostJson("/api/english/sentences", new { text = "No note here.", note = (string?)null }));

        Assert.Equal(JsonValueKind.String, s.GetProperty("note").ValueKind);
        Assert.Equal("", s.GetProperty("note").GetString());
    }

    // The client shows a bookmark toggle, so a double tap must not create a second row.
    // Note the status code differs from a first save: 200, not 201.
    [DbFact]
    public async Task Luu_trung_cau_thi_tra_lai_ban_ghi_cu_voi_ma_200()
    {
        var c = NewUser();

        var first = await c.PostJson("/api/english/sentences", new { text = "Exactly the same.", note = "lan dau" });
        var again = await c.PostJson("/api/english/sentences", new { text = "Exactly the same.", note = "lan hai" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var a = await Json.Read(first);
        var b = await Json.Read(again);
        Assert.Equal(a.GetProperty("id").GetGuid(), b.GetProperty("id").GetGuid());
        Assert.Equal("lan dau", b.GetProperty("note").GetString());   // the original note wins

        Assert.Equal(1, (await Json.Read(await c.GetAsync("/api/english/sentences"))).GetProperty("total").GetInt32());
    }

    [DbFact]
    public async Task Cau_rong_thi_400()
    {
        var res = await NewUser().PostJson("/api/english/sentences", new { text = "  ", note = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Thiếu nội dung câu.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Tim_kiem_khong_phan_biet_hoa_thuong_va_tim_ca_trong_ghi_chu()
    {
        var c = NewUser();
        await c.PostJson("/api/english/sentences", new { text = "The WEATHER is lovely.", note = "" });
        await c.PostJson("/api/english/sentences", new { text = "Nothing relevant here.", note = "noi ve thoi tiet" });
        await c.PostJson("/api/english/sentences", new { text = "Completely unrelated.", note = "" });

        // Lower-case needle against upper-case text: this only passes with ILIKE.
        var byText = await Json.Read(await c.GetAsync("/api/english/sentences?q=weather"));
        Assert.Equal(1, byText.GetProperty("total").GetInt32());

        var byNote = await Json.Read(await c.GetAsync("/api/english/sentences?q=thoi%20tiet"));
        Assert.Equal(1, byNote.GetProperty("total").GetInt32());

        // A blank q must not filter anything out.
        Assert.Equal(3, (await Json.Read(await c.GetAsync("/api/english/sentences?q=%20%20"))).GetProperty("total").GetInt32());
    }

    [DbFact]
    public async Task Xoa_cau_khong_ton_tai_thi_404()
    {
        var c = NewUser();
        var s = await Json.Read(await c.PostJson("/api/english/sentences", new { text = "Bye.", note = (string?)null }));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/english/sentences/{s.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/english/sentences/{Guid.NewGuid()}")).StatusCode);
    }
}
