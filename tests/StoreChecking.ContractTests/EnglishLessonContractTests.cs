using System.Net;
using System.Text.Json;

namespace StoreChecking.ContractTests;

/// <summary>
/// Bài học tiếng Anh theo chủ đề.
///
/// <para>Nội dung viết ở nơi khác rồi dán vào, nên thứ được giữ ở đây là <b>kết quả đã
/// tách hai cột</b>: câu tiếng Anh một bên, tiếng Việt một bên, đúng thứ tự. Tách được
/// mới ẩn được một bên để tự kiểm tra — đó là toàn bộ lý do không lưu nguyên khối chữ.</para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class EnglishLessonContractTests(ApiFactory api)
{
    private HttpClient NewUser() => api.ClientFor(Guid.NewGuid());

    private static object Line(string speaker, string en, string vi) =>
        new { kind = "line", speaker, en, vi };

    private static object Phrase(string en, string vi) =>
        new { kind = "phrase", speaker = (string?)null, en, vi };

    private static object Topic(string title, params object[] lines) =>
        new { title, lines };

    private static Task<HttpResponseMessage> Import(HttpClient c, params object[] topics) =>
        c.PostJson("/api/english/topics", new { topics });

    /// <summary>Một chủ đề mẫu, đúng hình dạng nội dung thật: hội thoại rồi tới cụm từ.</summary>
    private static async Task<Guid> NewTopic(HttpClient c, string title = "How's work going?")
    {
        var body = await Json.Ok(await Import(c, Topic(title,
            Line("teacher", "So, how's work going these days?", "Dạo này công việc thế nào?"),
            Line("you", "It's going well, thank you.", "Mọi thứ ổn, cảm ơn."),
            Phrase("these days", "dạo này"))));

        return body[0].GetProperty("id").GetGuid();
    }

    // ---------- Dán vào ----------

    [DbFact]
    public async Task Dan_mot_chu_de_roi_mo_lai_thay_dung_hinh_dang()
    {
        var c = NewUser();
        var id = await NewTopic(c);

        var t = await Json.Read(await c.GetAsync($"/api/english/topics/{id}"));
        Json.HasExactly(t, "id", "title", "lines", "createdAt", "updatedAt");
        Assert.Equal("How's work going?", t.GetProperty("title").GetString());

        var lines = t.GetProperty("lines");
        Assert.Equal(3, lines.GetArrayLength());

        Json.HasExactly(lines[0], "id", "kind", "speaker", "en", "vi");
        Assert.Equal("line", lines[0].GetProperty("kind").GetString());
        Assert.Equal("teacher", lines[0].GetProperty("speaker").GetString());
        Assert.Equal("Dạo này công việc thế nào?", lines[0].GetProperty("vi").GetString());

        // Cụm từ không có ai nói cả.
        Assert.Equal("phrase", lines[2].GetProperty("kind").GetString());
        Assert.Equal("", lines[2].GetProperty("speaker").GetString());
    }

    // Thứ tự là nội dung, không phải chuyện trang trí: hỏi rồi mới đáp. Trả về lộn xộn
    // thì bài hội thoại thành vô nghĩa.
    [DbFact]
    public async Task Giu_nguyen_thu_tu_dong_nhu_luc_dan()
    {
        var c = NewUser();
        var id = (await Json.Ok(await Import(c, Topic("Thứ tự",
            Line("teacher", "A", "a"),
            Line("you", "B", "b"),
            Line("teacher", "C", "c"),
            Line("you", "D", "d"),
            Phrase("E", "e")))))[0].GetProperty("id").GetGuid();

        var lines = (await Json.Read(await c.GetAsync($"/api/english/topics/{id}"))).GetProperty("lines");
        Assert.Equal(["A", "B", "C", "D", "E"],
            lines.EnumerateArray().Select(x => x.GetProperty("en").GetString()!).ToArray());
    }

    // Một lần dán thường mang nhiều chủ đề — nguồn là cả một cuộc hội thoại, không phải
    // một bài lẻ.
    [DbFact]
    public async Task Dan_nhieu_chu_de_mot_luot()
    {
        var c = NewUser();

        var made = await Json.Ok(await Import(c,
            Topic("Chủ đề 1", Line("teacher", "One", "Một")),
            Topic("Chủ đề 2", Line("teacher", "Two", "Hai")),
            Topic("Chủ đề 3", Line("you", "Three", "Ba"))));

        Assert.Equal(3, made.GetArrayLength());

        var list = await Json.Read(await c.GetAsync("/api/english/topics"));
        Assert.Equal(3, list.GetArrayLength());
    }

    [DbFact]
    public async Task Danh_sach_dem_so_dong_va_so_cum_tu()
    {
        var c = NewUser();
        await Import(c, Topic("Đếm",
            Line("teacher", "A", "a"),
            Line("you", "B", "b"),
            Phrase("x", "1"),
            Phrase("y", "2"),
            Phrase("z", "3")));

        var row = (await Json.Read(await c.GetAsync("/api/english/topics")))[0];
        Json.HasExactly(row, "id", "title", "lineCount", "phraseCount", "createdAt", "updatedAt");
        Assert.Equal(2, row.GetProperty("lineCount").GetInt32());
        Assert.Equal(3, row.GetProperty("phraseCount").GetInt32());
    }

    // Dán thêm thì bài mới lên đầu — chỗ người vừa dán xong sẽ tìm.
    [DbFact]
    public async Task Chu_de_dan_sau_nam_tren_dau()
    {
        var c = NewUser();
        await Import(c, Topic("Cũ", Line("teacher", "old", "cũ")));
        await Import(c, Topic("Mới", Line("teacher", "new", "mới")));

        var list = await Json.Read(await c.GetAsync("/api/english/topics"));
        Assert.Equal(["Mới", "Cũ"],
            list.EnumerateArray().Select(x => x.GetProperty("title").GetString()!).ToArray());
    }

    // Cả mẻ vào hết hoặc không vào gì. Vào được một nửa thì người dùng phải tự dò xem
    // nửa nào đã vào, từ nội dung phải đọc lại mới nhận ra.
    [DbFact]
    public async Task Mot_chu_de_hong_thi_ca_me_khong_vao()
    {
        var c = NewUser();

        var res = await Import(c,
            Topic("Tốt", Line("teacher", "ok", "ổn")),
            Topic("Hỏng", new { kind = "line", speaker = "nobody", en = "x", vi = "y" }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty((await Json.Read(await c.GetAsync("/api/english/topics"))).EnumerateArray());
    }

    // ---------- Chặn dữ liệu sai hình dạng ----------

    [DbFact]
    public async Task Chu_de_khong_ten_thi_400()
    {
        var res = await Import(NewUser(), Topic("   ", Line("teacher", "x", "y")));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Chủ đề phải có tên.", (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Chu_de_khong_co_dong_nao_thi_400()
    {
        var res = await NewUser().PostJson("/api/english/topics",
            new { topics = new[] { new { title = "Rỗng", lines = Array.Empty<object>() } } });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Chủ đề phải có ít nhất một dòng.",
            (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Dong_khong_co_tieng_Anh_thi_400()
    {
        var res = await Import(NewUser(), Topic("X", Line("teacher", "   ", "có nghĩa")));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Dòng nào cũng phải có phần tiếng Anh.",
            (await Json.Read(res)).GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Cau_hoi_thoai_phai_co_nguoi_noi()
    {
        var res = await Import(NewUser(), Topic("X", Line("", "Hello", "Chào")));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Câu hội thoại phải là của 'teacher' hoặc 'you'.",
            (await Json.Read(res)).GetProperty("error").GetString());
    }

    // Cụm từ mang theo người nói thì giao diện sẽ vẽ nó thành câu thoại. Bỏ đi chứ không
    // từ chối: client gửi thừa một trường rỗng là chuyện thường.
    [DbFact]
    public async Task Cum_tu_co_gui_kem_nguoi_noi_thi_bi_bo()
    {
        var c = NewUser();
        var id = (await Json.Read(await Import(c, Topic("X",
            new { kind = "phrase", speaker = "teacher", en = "these days", vi = "dạo này" }))))[0]
            .GetProperty("id").GetGuid();

        var lines = (await Json.Read(await c.GetAsync($"/api/english/topics/{id}"))).GetProperty("lines");
        Assert.Equal("", lines[0].GetProperty("speaker").GetString());
    }

    [DbFact]
    public async Task Dan_danh_sach_rong_thi_400()
    {
        var res = await NewUser().PostJson("/api/english/topics", new { topics = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Không có chủ đề nào để lưu.",
            (await Json.Read(res)).GetProperty("error").GetString());
    }

    // ---------- Sửa ----------

    [DbFact]
    public async Task Sua_ca_chu_de_mot_luot()
    {
        var c = NewUser();
        var id = await NewTopic(c);

        var res = await c.PutJson($"/api/english/topics/{id}", new
        {
            title = "Tên mới",
            lines = new[]
            {
                Line("teacher", "Sửa rồi", "Đã sửa"),
                Phrase("brand new", "mới tinh"),
            },
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var t = await Json.Read(res);
        Assert.Equal("Tên mới", t.GetProperty("title").GetString());

        // Dòng cũ đi hẳn, không cộng dồn.
        var lines = t.GetProperty("lines");
        Assert.Equal(2, lines.GetArrayLength());
        Assert.Equal("Sửa rồi", lines[0].GetProperty("en").GetString());

        var again = await Json.Read(await c.GetAsync($"/api/english/topics/{id}"));
        Assert.Equal(2, again.GetProperty("lines").GetArrayLength());
    }

    [DbFact]
    public async Task Sua_chu_de_khong_ton_tai_thi_404()
    {
        var res = await NewUser().PutJson($"/api/english/topics/{Guid.NewGuid()}", new
        {
            title = "X",
            lines = new[] { Line("teacher", "a", "b") },
        });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ---------- Xoá ----------

    [DbFact]
    public async Task Xoa_chu_de_thi_moi_dong_di_theo()
    {
        var c = NewUser();
        var id = await NewTopic(c);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/english/topics/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/english/topics/{id}")).StatusCode);
        Assert.Empty((await Json.Read(await c.GetAsync("/api/english/topics"))).EnumerateArray());
    }

    [DbFact]
    public async Task Xoa_chu_de_khong_ton_tai_thi_404()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await NewUser().DeleteAsync($"/api/english/topics/{Guid.NewGuid()}")).StatusCode);
    }

    // ---------- Cách ly hai người dùng ----------

    [DbFact]
    public async Task Nguoi_khac_khong_thay_khong_sua_khong_xoa_duoc_bai_cua_minh()
    {
        var a = NewUser();
        var b = NewUser();
        var id = await NewTopic(a);

        Assert.Empty((await Json.Read(await b.GetAsync("/api/english/topics"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/english/topics/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/english/topics/{id}")).StatusCode);

        var put = await b.PutJson($"/api/english/topics/{id}", new
        {
            title = "Chiếm",
            lines = new[] { Line("teacher", "x", "y") },
        });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        // Và bài của A còn nguyên.
        var t = await Json.Read(await a.GetAsync($"/api/english/topics/{id}"));
        Assert.Equal("How's work going?", t.GetProperty("title").GetString());
    }

    [DbFact]
    public async Task Chua_dang_nhap_thi_khong_doc_duoc()
    {
        var anon = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/english/topics")).StatusCode);
    }
}
