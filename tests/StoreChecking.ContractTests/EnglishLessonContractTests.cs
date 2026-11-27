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
        Json.HasExactly(t, "id", "title", "kind", "lines", "createdAt", "updatedAt");
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
        Json.HasExactly(row, "id", "title", "kind", "lineCount", "phraseCount", "studying",
            "categoryId", "createdAt", "updatedAt");
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

    // ---------- Dấu "đang học dang dở" ----------

    /// <summary>Lấy một chủ đề ra khỏi danh sách, theo id.</summary>
    private static async Task<JsonElement> InList(HttpClient c, Guid id)
    {
        var list = await Json.Read(await c.GetAsync("/api/english/topics"));
        return list.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == id);
    }

    [DbFact]
    public async Task Bai_vua_dan_vao_thi_chua_danh_dau()
    {
        var c = NewUser();
        var id = await NewTopic(c);

        Assert.False((await InList(c, id)).GetProperty("studying").GetBoolean());
    }

    [DbFact]
    public async Task Danh_dau_dang_hoc_roi_mo_lai_van_con()
    {
        var c = NewUser();
        var id = await NewTopic(c);

        var res = await c.PutJson($"/api/english/topics/{id}/studying", new { studying = true });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // Trả về chính hàng của danh sách, để giao diện khỏi phải nạp lại cả danh sách.
        var back = await Json.Read(res);
        Assert.True(back.GetProperty("studying").GetBoolean());
        Assert.Equal("How's work going?", back.GetProperty("title").GetString());
        Assert.Equal(2, back.GetProperty("lineCount").GetInt32());
        Assert.Equal(1, back.GetProperty("phraseCount").GetInt32());

        Assert.True((await InList(c, id)).GetProperty("studying").GetBoolean());
    }

    [DbFact]
    public async Task Bo_dau_thi_het_dang_hoc()
    {
        var c = NewUser();
        var id = await NewTopic(c);

        await c.PutJson($"/api/english/topics/{id}/studying", new { studying = true });
        await c.PutJson($"/api/english/topics/{id}/studying", new { studying = false });

        Assert.False((await InList(c, id)).GetProperty("studying").GetBoolean());
    }

    /// <summary>
    /// ⚠️ Chốt chính: <b>sửa bài KHÔNG được xoá dấu</b>.
    ///
    /// <para>Sửa một lỗi chính tả trong bài không phải là tuyên bố rằng đã học xong nó.
    /// Mà bài hay bị sửa nhất lại chính là bài đang học dở — nên nếu lưu bài mà dấu bay
    /// mất thì dấu đó hỏng đúng ở chỗ duy nhất nó có việc.</para>
    /// </summary>
    [DbFact]
    public async Task Sua_bai_thi_dau_dang_hoc_NAM_YEN()
    {
        var c = NewUser();
        var id = await NewTopic(c);
        await c.PutJson($"/api/english/topics/{id}/studying", new { studying = true });

        var res = await c.PutJson($"/api/english/topics/{id}", new
        {
            title = "Tên mới",
            lines = new[] { Line("teacher", "Sửa rồi", "Đã sửa") },
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var row = await InList(c, id);
        Assert.Equal("Tên mới", row.GetProperty("title").GetString());
        Assert.True(row.GetProperty("studying").GetBoolean());
    }

    /// <summary>
    /// Đánh dấu nhiều bài cùng lúc là chuyện bình thường, và đánh dấu bài này không được
    /// lặng lẽ bỏ dấu bài kia — đó là lý do cờ nằm trên từng bài chứ không phải một ô
    /// "bài đang học" duy nhất.
    /// </summary>
    [DbFact]
    public async Task Danh_dau_nhieu_bai_cung_luc_duoc()
    {
        var c = NewUser();
        var mot = await NewTopic(c, "Bài một");
        var hai = await NewTopic(c, "Bài hai");

        await c.PutJson($"/api/english/topics/{mot}/studying", new { studying = true });
        await c.PutJson($"/api/english/topics/{hai}/studying", new { studying = true });

        Assert.True((await InList(c, mot)).GetProperty("studying").GetBoolean());
        Assert.True((await InList(c, hai)).GetProperty("studying").GetBoolean());
    }

    [DbFact]
    public async Task Danh_dau_chu_de_khong_ton_tai_thi_404()
    {
        var res = await NewUser()
            .PutJson($"/api/english/topics/{Guid.NewGuid()}/studying", new { studying = true });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [DbFact]
    public async Task Nguoi_khac_khong_danh_dau_duoc_bai_cua_minh()
    {
        var a = NewUser();
        var b = NewUser();
        var id = await NewTopic(a);

        var res = await b.PutJson($"/api/english/topics/{id}/studying", new { studying = true });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);

        Assert.False((await InList(a, id)).GetProperty("studying").GetBoolean());
    }

    // ---------- Bài luyện dịch ----------

    private static object Para(string vi, string en) =>
        new { kind = "para", speaker = (string?)null, en, vi };

    private static Task<HttpResponseMessage> ImportTranslate(HttpClient c, params object[] topics) =>
        c.PostJson("/api/english/topics", new { topics, kind = "translate" });

    /// <summary>Một bài dịch mẫu: đoạn văn rồi tới bảng từ gợi ý.</summary>
    private static async Task<Guid> NewTranslate(HttpClient c, string title = "Thói quen thức khuya")
    {
        var body = await Json.Ok(await ImportTranslate(c, Topic(title,
            Para("Suốt thời sinh viên, tôi có thói quen thức đến hai, ba giờ sáng.",
                 "All through university I used to stay up until two or three in the morning."),
            Phrase("stay up", "thức"),
            Phrase("in fact", "thật ra"))));

        return body[0].GetProperty("id").GetGuid();
    }

    [DbFact]
    public async Task Luu_bai_dich_roi_mo_lai_thay_dung_hinh_dang()
    {
        var c = NewUser();
        var id = await NewTranslate(c);

        var t = await Json.Read(await c.GetAsync($"/api/english/topics/{id}"));
        Assert.Equal("translate", t.GetProperty("kind").GetString());

        var lines = t.GetProperty("lines");
        Assert.Equal(3, lines.GetArrayLength());
        Assert.Equal("para", lines[0].GetProperty("kind").GetString());
        Assert.StartsWith("Suốt thời sinh viên", lines[0].GetProperty("vi").GetString());
        Assert.StartsWith("All through university", lines[0].GetProperty("en").GetString());
        Assert.Equal("phrase", lines[1].GetProperty("kind").GetString());
    }

    /// <summary>
    /// ⚠️ Chốt chính: <b>hai mục KHÔNG được lẫn vào nhau</b>.
    ///
    /// <para>Bài hội thoại và bài dịch được vẽ ra màn hình theo hai cách khác hẳn nhau,
    /// nên một chủ đề nằm nhầm mục thì hiện ra SAI chứ không phải hiện ra xấu.</para>
    /// </summary>
    [DbFact]
    public async Task Hai_muc_khong_lan_vao_nhau()
    {
        var c = NewUser();
        await NewTopic(c, "Bài hội thoại");
        await NewTranslate(c, "Bài dịch");

        var lessons = await Json.Read(await c.GetAsync("/api/english/topics?kind=lesson"));
        Assert.Equal(["Bài hội thoại"],
            lessons.EnumerateArray().Select(x => x.GetProperty("title").GetString()!).ToArray());

        var translate = await Json.Read(await c.GetAsync("/api/english/topics?kind=translate"));
        Assert.Equal(["Bài dịch"],
            translate.EnumerateArray().Select(x => x.GetProperty("title").GetString()!).ToArray());
    }

    /// <summary>
    /// Không ghi `kind` thì phải ra đúng những gì client cũ vẫn nhận — mục Bài học.
    /// Mặc định thành "tất cả" là mục Bài học tự nhiên mọc thêm bài dịch mà không ai sửa gì.
    /// </summary>
    [DbFact]
    public async Task Khong_ghi_muc_thi_mac_dinh_la_bai_hoc()
    {
        var c = NewUser();
        await NewTopic(c, "Bài hội thoại");
        await NewTranslate(c, "Bài dịch");

        var list = await Json.Read(await c.GetAsync("/api/english/topics"));
        Assert.Equal(["Bài hội thoại"],
            list.EnumerateArray().Select(x => x.GetProperty("title").GetString()!).ToArray());

        // Và dán vào mà không ghi mục thì cũng vào mục Bài học.
        await Import(c, Topic("Dán trần", Line("teacher", "x", "y")));
        var again = await Json.Read(await c.GetAsync("/api/english/topics"));
        Assert.Equal(2, again.GetArrayLength());
    }

    /// <summary>
    /// ⚠️ Cả bài dịch đứng trên phần tiếng Việt: đó là ĐỀ. Thiếu nó thì bài còn mỗi bản
    /// tiếng Anh — không còn gì để dịch, mà nhìn vẫn như một bài hợp lệ.
    /// </summary>
    [DbFact]
    public async Task Doan_van_khong_co_tieng_Viet_thi_400()
    {
        var res = await ImportTranslate(NewUser(), Topic("Thiếu đề",
            Para("   ", "All through university I used to stay up late."),
            Phrase("stay up", "thức")));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [DbFact]
    public async Task Muc_la_gi_do_khong_biet_thi_400()
    {
        var res = await NewUser().PostJson("/api/english/topics",
            new { topics = new[] { Topic("X", Line("teacher", "a", "b")) }, kind = "linh tinh" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [DbFact]
    public async Task Loai_dong_la_gi_do_khong_biet_thi_400()
    {
        var res = await ImportTranslate(NewUser(), Topic("X",
            new { kind = "doan", speaker = (string?)null, en = "a", vi = "b" }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// ⚠️ Sửa bài KHÔNG được làm nó đổi mục. Chủ đề không chuyển sang danh sách khác chỉ
    /// vì ai đó sửa chữ trong nó, mà mục kia thì không vẽ nổi bài này.
    /// </summary>
    [DbFact]
    public async Task Sua_bai_thi_muc_NAM_YEN()
    {
        var c = NewUser();
        var id = await NewTranslate(c);

        var res = await c.PutJson($"/api/english/topics/{id}", new
        {
            title = "Tên mới",
            lines = new[] { Para("Đề mới.", "A new passage.") },
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("translate", (await Json.Read(res)).GetProperty("kind").GetString());

        var list = await Json.Read(await c.GetAsync("/api/english/topics?kind=translate"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("Tên mới", list[0].GetProperty("title").GetString());
    }

    [DbFact]
    public async Task Bai_dich_cung_danh_dau_dang_hoc_duoc()
    {
        var c = NewUser();
        var id = await NewTranslate(c);

        await c.PutJson($"/api/english/topics/{id}/studying", new { studying = true });

        var list = await Json.Read(await c.GetAsync("/api/english/topics?kind=translate"));
        Assert.True(list[0].GetProperty("studying").GetBoolean());
    }


    // ---------- Phân loại chủ đề ----------

    private static async Task<Guid> NewCategory(HttpClient c, string name) =>
        (await Json.Ok(await c.PostJson("/api/english/categories", new { name })))
            .GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> FileUnder(HttpClient c, Guid topic, Guid? category) =>
        c.PutJson($"/api/english/topics/{topic}/category", new { categoryId = category });

    [DbFact]
    public async Task Them_loai_roi_doc_lai_thay_dung_hinh_dang()
    {
        var c = NewUser();

        var made = await Json.Ok(
            await c.PostJson("/api/english/categories", new { name = "  Công việc  " }));
        Json.HasExactly(made, "id", "name", "createdAt");
        Assert.Equal("Công việc", made.GetProperty("name").GetString());

        var list = await Json.Read(await c.GetAsync("/api/english/categories"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("Công việc", list[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// ⚠️ Hai loại cùng tên là lỗi gõ, không phải loại mới — ô chọn sẽ có hai dòng
    /// giống hệt nhau mà không cách nào biết bài nào đi đâu. Đây là phép so chuỗi DUY NHẤT
    /// trong cả tính năng, và nó chỉ chạy đúng lúc tạo.
    /// </summary>
    [DbFact]
    public async Task Trung_ten_loai_thi_tu_choi_du_lech_hoa_thuong_va_khoang_trang()
    {
        var c = NewUser();
        await NewCategory(c, "Công việc");

        var res = await c.PostJson("/api/english/categories", new { name = "  CÔNG VIỆC " });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var list = await Json.Read(await c.GetAsync("/api/english/categories"));
        Assert.Equal(1, list.GetArrayLength());
    }

    [DbFact]
    public async Task Loai_phai_co_ten()
    {
        var c = NewUser();

        var res = await c.PostJson("/api/english/categories", new { name = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// Đây là lý do loại là một BẢNG chứ không phải một chuỗi nằm trên từng chủ đề: chủ đề
    /// giữ <c>id</c> nên đổi tên loại là UPDATE một dòng, không phải sửa từng bài rồi sót một cái.
    /// </summary>
    [DbFact]
    public async Task Doi_ten_loai_thi_bai_van_thuoc_dung_loai_do()
    {
        var c = NewUser();
        var cat = await NewCategory(c, "Công việc");
        var id = await NewTopic(c);
        await FileUnder(c, id, cat);

        var renamed = await Json.Ok(
            await c.PutJson($"/api/english/categories/{cat}", new { name = "Đi làm" }));
        Assert.Equal("Đi làm", renamed.GetProperty("name").GetString());

        var row = (await Json.Read(await c.GetAsync("/api/english/topics")))[0];
        Assert.Equal(cat, row.GetProperty("categoryId").GetGuid());
    }

    /// <summary>
    /// Phép kiểm trùng tên phải BỎ QUA chính nó, nếu không thì sửa hoa thường của một
    /// loại ("Công Việc" → "Công việc") bị báo là trùng với chính nó và không sửa được.
    /// </summary>
    [DbFact]
    public async Task Doi_hoa_thuong_cua_chinh_no_thi_van_luu_duoc()
    {
        var c = NewUser();
        var cat = await NewCategory(c, "Công Việc");

        var fixedUp = await Json.Ok(
            await c.PutJson($"/api/english/categories/{cat}", new { name = "Công việc" }));
        Assert.Equal("Công việc", fixedUp.GetProperty("name").GetString());
    }

    /// <summary>
    /// ⚠️ Xoá cái nhãn không phải xoá bài. <c>cascade</c> ở đây là một nút "xoá hết bài
    /// trong nhóm" nằm ngay cạnh nút Đổi tên, không có gì báo trước.
    /// </summary>
    [DbFact]
    public async Task Xoa_loai_thi_bai_O_LAI_va_ve_chua_phan_loai()
    {
        var c = NewUser();
        var cat = await NewCategory(c, "Công việc");
        var id = await NewTopic(c);
        await FileUnder(c, id, cat);

        var gone = await c.DeleteAsync($"/api/english/categories/{cat}");
        Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);

        var list = await Json.Read(await c.GetAsync("/api/english/topics"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(id, list[0].GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, list[0].GetProperty("categoryId").ValueKind);
    }

    /// <summary>
    /// ⚠️ Cột <c>updatedAt</c> nghĩa là "bài đã bị sửa", và đó cũng là ngày danh sách
    /// hiện ra. Xếp một bài vào mục thì không sửa gì trong bài cả.
    /// </summary>
    [DbFact]
    public async Task Xep_vao_loai_thi_khong_doi_updatedAt()
    {
        var c = NewUser();
        var cat = await NewCategory(c, "Công việc");
        var id = await NewTopic(c);

        var before = (await Json.Read(await c.GetAsync("/api/english/topics")))[0]
            .GetProperty("updatedAt").GetDateTimeOffset();

        await FileUnder(c, id, cat);

        var row = (await Json.Read(await c.GetAsync("/api/english/topics")))[0];
        Assert.Equal(before, row.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal(cat, row.GetProperty("categoryId").GetGuid());
    }

    /// <summary>
    /// ⚠️ Sửa một lỗi chính tả không phải tuyên bố bài này không thuộc mục nào nữa — cùng
    /// lý do với dấu "đang học". Đó là vì sao phân loại có endpoint riêng.
    /// </summary>
    [DbFact]
    public async Task Sua_bai_thi_KHONG_bo_phan_loai()
    {
        var c = NewUser();
        var cat = await NewCategory(c, "Công việc");
        var id = await NewTopic(c);
        await FileUnder(c, id, cat);

        await c.PutJson($"/api/english/topics/{id}", new
        {
            title = "Tên mới",
            lines = new[] { Line("teacher", "Hello.", "Xin chào.") },
        });

        var row = (await Json.Read(await c.GetAsync("/api/english/topics")))[0];
        Assert.Equal("Tên mới", row.GetProperty("title").GetString());
        Assert.Equal(cat, row.GetProperty("categoryId").GetGuid());
    }

    [DbFact]
    public async Task Bo_phan_loai_bang_null()
    {
        var c = NewUser();
        var cat = await NewCategory(c, "Công việc");
        var id = await NewTopic(c);
        await FileUnder(c, id, cat);

        var back = await Json.Ok(await FileUnder(c, id, null));
        Assert.Equal(JsonValueKind.Null, back.GetProperty("categoryId").ValueKind);
    }

    /// <summary>
    /// ⚠️ Loại của người khác phải bị TỪ CHỐI chứ không phải lưu lặng lẽ: lưu được thì
    /// trong database bài mang một id không tra ra được, còn trên màn hình nó đọc là "chưa phân
    /// loại" — hai thứ nói ngược nhau mà không có gì báo.
    /// </summary>
    [DbFact]
    public async Task Loai_cua_nguoi_khac_thi_tu_choi()
    {
        var mine = NewUser();
        var other = NewUser();
        var theirs = await NewCategory(other, "Của người khác");
        var id = await NewTopic(mine);

        var res = await FileUnder(mine, id, theirs);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var row = (await Json.Read(await mine.GetAsync("/api/english/topics")))[0];
        Assert.Equal(JsonValueKind.Null, row.GetProperty("categoryId").ValueKind);
    }

    /// <summary>
    /// Một danh sách loại dùng chung cho cả hai mục: loại nói về nội dung ("Du lịch"), không
    /// nói về kiểu bài. Tách đôi thì cùng một loại phải tạo hai lần rồi hai bên trôi khác nhau.
    /// </summary>
    [DbFact]
    public async Task Mot_danh_sach_loai_dung_chung_cho_ca_hai_muc()
    {
        var c = NewUser();
        var cat = await NewCategory(c, "Du lịch");
        var lesson = await NewTopic(c);
        var translate = await NewTranslate(c);

        Assert.Equal(HttpStatusCode.OK, (await FileUnder(c, lesson, cat)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FileUnder(c, translate, cat)).StatusCode);

        var lessons = await Json.Read(await c.GetAsync("/api/english/topics?kind=lesson"));
        var dich = await Json.Read(await c.GetAsync("/api/english/topics?kind=translate"));
        Assert.Equal(cat, lessons[0].GetProperty("categoryId").GetGuid());
        Assert.Equal(cat, dich[0].GetProperty("categoryId").GetGuid());
    }

    [DbFact]
    public async Task Khong_thay_loai_cua_nguoi_khac()
    {
        var mine = NewUser();
        var other = NewUser();
        await NewCategory(other, "Của người khác");

        var list = await Json.Read(await mine.GetAsync("/api/english/categories"));
        Assert.Equal(0, list.GetArrayLength());
    }


    // ---------- Bài phỏng vấn kỹ thuật: dòng 'level' và 'note' ----------

    private static object Level(string en, string vi = "") =>
        new { kind = "level", speaker = (string?)null, en, vi };

    private static object Note(string vi) =>
        new { kind = "note", speaker = (string?)null, en = "", vi };

    private const string Code =
        "Đây là câu phân biệt junior và middle.\n\ncsharp\nint a = 5;\nint b = a;   // copy giá trị\n\nLưu ý: string là reference type.";

    [DbFact]
    public async Task Bai_phong_van_luu_duoc_ca_muc_va_khoi_giai_thich()
    {
        var c = NewUser();
        var made = await Json.Ok(await Import(c, Topic("C# Fundamentals & OOP",
            Level("Junior"),
            Line("teacher", "What's the difference between value types and reference types?",
                 "Value type và reference type khác nhau thế nào?"),
            Line("you", "A value type holds its data directly.",
                 "Value type chứa trực tiếp dữ liệu."),
            Note(Code),
            Level("Middle", "câu hỏi đào sâu"),
            Phrase("holds its data directly", "chứa trực tiếp dữ liệu"))));

        var t = await Json.Read(await c.GetAsync($"/api/english/topics/{made[0].GetProperty("id").GetGuid()}"));
        var lines = t.GetProperty("lines");
        Assert.Equal(6, lines.GetArrayLength());

        Assert.Equal("level", lines[0].GetProperty("kind").GetString());
        Assert.Equal("Junior", lines[0].GetProperty("en").GetString());
        Assert.Equal("", lines[0].GetProperty("speaker").GetString());

        Assert.Equal("note", lines[3].GetProperty("kind").GetString());
        Assert.Equal("", lines[3].GetProperty("en").GetString());

        Assert.Equal("Middle", lines[4].GetProperty("en").GetString());
        Assert.Equal("câu hỏi đào sâu", lines[4].GetProperty("vi").GetString());
    }

    /// <summary>
    /// ⚠️ Khối giải thích phải giữ nguyên xuống dòng và thụt đầu dòng. Đó là thứ duy nhất
    /// làm một đoạn code dán vào còn đọc được; nối thành một dòng là mất hết.
    /// </summary>
    [DbFact]
    public async Task Khoi_giai_thich_giu_nguyen_xuong_dong()
    {
        var c = NewUser();
        var made = await Json.Ok(await Import(c, Topic("Giữ dòng",
            Line("teacher", "Hello.", "Xin chào."),
            Note(Code))));

        var t = await Json.Read(await c.GetAsync($"/api/english/topics/{made[0].GetProperty("id").GetGuid()}"));
        var vi = t.GetProperty("lines")[1].GetProperty("vi").GetString()!;

        Assert.Equal(Code, vi);
        Assert.Contains("\nint a = 5;\n", vi);
    }

    /// <summary>
    /// 'note' là loại dòng DUY NHẤT được phép rỗng phần tiếng Anh. Mọi loại khác vẫn phải
    /// có, không thì một dòng hội thoại trống lọt qua và bài có một ô rỗng không ai giải thích được.
    /// </summary>
    [DbFact]
    public async Task Chi_rieng_note_duoc_rong_phan_tieng_Anh()
    {
        var c = NewUser();

        var trong = await Import(c, Topic("Trống", Line("teacher", "   ", "Xin chào.")));
        Assert.Equal(HttpStatusCode.BadRequest, trong.StatusCode);

        var okRes = await Import(c, Topic("Được",
            Line("teacher", "Hello.", "Xin chào."), Note("Giải thích.")));
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
    }

    /// <summary>
    /// ⚠️ Khối giải thích rỗng trơn là một ô trống trên màn hình, không phân biệt được với
    /// một lần tách đã lặng lẽ làm mất chữ.
    /// </summary>
    [DbFact]
    public async Task Khoi_giai_thich_rong_thi_tu_choi()
    {
        var c = NewUser();

        var res = await Import(c, Topic("Rỗng",
            Line("teacher", "Hello.", "Xin chào."), Note("   ")));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// ⚠️ Loại dòng mới nào cũng phải có nhánh trong CHECK `speaker_fits_kind`: CASE không
    /// khớp nhánh nào thì trả NULL, mà <b>CHECK gặp NULL là coi như đạt</b>. Lúc đó một dòng
    /// 'level' mang speaker='teacher' lọt qua và giao diện vẽ nó thành câu thoại.
    /// </summary>
    [DbFact]
    public async Task Dong_level_va_note_khong_mang_nguoi_noi()
    {
        var c = NewUser();
        var made = await Json.Ok(await Import(c, Topic("Người nói",
            new { kind = "level", speaker = "teacher", en = "Junior", vi = "" },
            new { kind = "note", speaker = "you", en = "", vi = "Giải thích." })));

        var t = await Json.Read(await c.GetAsync($"/api/english/topics/{made[0].GetProperty("id").GetGuid()}"));
        Assert.Equal("", t.GetProperty("lines")[0].GetProperty("speaker").GetString());
        Assert.Equal("", t.GetProperty("lines")[1].GetProperty("speaker").GetString());
    }

    [DbFact]
    public async Task Loai_dong_la_thi_tu_choi()
    {
        var c = NewUser();

        var res = await Import(c, Topic("Lạ",
            new { kind = "quiz", speaker = (string?)null, en = "x", vi = "y" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// Đếm trên danh sách chỉ đếm câu thoại và cụm từ. Dòng mức và khối giải thích không
    /// phải thứ để học thuộc, nên cộng vào là con số trên màn hình không còn đúng với cái gì cả.
    /// </summary>
    [DbFact]
    public async Task Dem_tren_danh_sach_bo_qua_level_va_note()
    {
        var c = NewUser();
        await Import(c, Topic("Đếm",
            Level("Junior"),
            Line("teacher", "A", "a"),
            Note("Giải thích."),
            Phrase("x", "1")));

        var row = (await Json.Read(await c.GetAsync("/api/english/topics")))[0];
        Assert.Equal(1, row.GetProperty("lineCount").GetInt32());
        Assert.Equal(1, row.GetProperty("phraseCount").GetInt32());
    }

    [DbFact]
    public async Task Chua_dang_nhap_thi_khong_doc_duoc()
    {
        var anon = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/english/topics")).StatusCode);
    }
}
