using System.Net;
using System.Text.Json;

namespace StoreChecking.ContractTests;

/// <summary>
/// Công tắc tự đăng video lên Fanpage.
///
/// <para>Trước đây nó nằm ở biến môi trường; giờ nằm trong database để một cái checkbox
/// đổi được ngay. Điều đáng test không phải "lưu được không" mà là <b>mặc định</b> và
/// <b>tách riêng theo người</b> — sai một trong hai thì bài sẽ tự lên Fanpage của người
/// không hề bật nó.</para>
/// </summary>
[Collection(nameof(ApiCollection))]
public class VideoSettingsContractTests(ApiFactory app)
{
    private HttpClient NewUser() => app.ClientFor(Guid.NewGuid());

    [DbFact]
    public async Task Mac_dinh_la_BAT()
    {
        var s = await Json.Read(await NewUser().GetAsync("/api/media/settings"));

        Json.HasExactly(s, "autoPost", "fanpageReady", "makeVideos", "makePosts");

        // Người dùng yêu cầu bật sẵn. Chưa ai đụng tới cài đặt thì vẫn phải là true.
        Assert.True(s.GetProperty("autoPost").GetBoolean());
        Assert.True(s.GetProperty("makeVideos").GetBoolean());
        Assert.True(s.GetProperty("makePosts").GetBoolean());

        // Máy chủ chạy test không khai khoá Facebook, nên phải báo là chưa sẵn sàng — đó là
        // thứ giao diện dựa vào để nói rõ vì sao bật mà không có bài nào lên.
        Assert.False(s.GetProperty("fanpageReady").GetBoolean());
    }

    [DbFact]
    public async Task Tat_roi_bat_lai_thi_nho_dung_trang_thai()
    {
        var c = NewUser();

        var off = await Json.Read(await c.PutJson("/api/media/settings/auto-post", new { on = false }));
        Assert.False(off.GetProperty("autoPost").GetBoolean());

        // Đọc lại từ database, không phải tin vào câu trả lời của lần ghi.
        var again = await Json.Read(await c.GetAsync("/api/media/settings"));
        Assert.False(again.GetProperty("autoPost").GetBoolean());

        var on = await Json.Read(await c.PutJson("/api/media/settings/auto-post", new { on = true }));
        Assert.True(on.GetProperty("autoPost").GetBoolean());

        // Bật rồi tắt rồi bật lại không được đẻ ra dòng thứ hai — khoá chính là (user, key),
        // nếu sai thì lần ghi thứ hai sẽ ném lỗi trùng khoá chứ không im lặng.
        Assert.Equal(HttpStatusCode.OK,
            (await c.PutJson("/api/media/settings/auto-post", new { on = true })).StatusCode);
    }

    // Cài đặt của người này KHÔNG được rò sang người kia. Sai chỗ này nghĩa là video của
    // một người tự lên Fanpage vì người khác bật công tắc.
    [DbFact]
    public async Task Cai_dat_cua_ai_nguoi_nay_giu()
    {
        var a = NewUser();
        var b = NewUser();

        await a.PutJson("/api/media/settings/auto-post", new { on = false });

        var mine = await Json.Read(await a.GetAsync("/api/media/settings"));
        var theirs = await Json.Read(await b.GetAsync("/api/media/settings"));

        Assert.False(mine.GetProperty("autoPost").GetBoolean());
        Assert.True(theirs.GetProperty("autoPost").GetBoolean());
    }

    [DbFact]
    public async Task Chua_dang_nhap_thi_khong_doc_duoc()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await app.AnonymousClient().GetAsync("/api/media/settings")).StatusCode);
    }

    // Ba công tắc phải ĐỘC LẬP. Gộp nhầm thì tắt "tạo bài" sẽ tắt luôn cả video, hoặc tệ
    // hơn: tắt "tạo video" mà bài vẫn tự lên Fanpage.
    [DbFact]
    public async Task Ba_cong_tac_khong_dinh_vao_nhau()
    {
        var c = NewUser();

        await c.PutJson("/api/media/settings/make-posts", new { on = false });

        var s = await Json.Read(await c.GetAsync("/api/media/settings"));
        Assert.False(s.GetProperty("makePosts").GetBoolean());
        Assert.True(s.GetProperty("makeVideos").GetBoolean());
        Assert.True(s.GetProperty("autoPost").GetBoolean());

        await c.PutJson("/api/media/settings/make-videos", new { on = false });

        s = await Json.Read(await c.GetAsync("/api/media/settings"));
        Assert.False(s.GetProperty("makeVideos").GetBoolean());
        Assert.False(s.GetProperty("makePosts").GetBoolean());
        Assert.True(s.GetProperty("autoPost").GetBoolean());
    }

    [DbFact]
    public async Task Cong_tac_la_khong_co_thi_bao_404()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await NewUser().PutJson("/api/media/settings/khong-co-that", new { on = true })).StatusCode);
    }

    // Kho ảnh rỗng thì phải nói rõ, không được ném lỗi 500 khó hiểu.
    [DbFact]
    public async Task Chua_co_anh_thi_viet_bai_bao_ro_ly_do()
    {
        var res = await NewUser().PostAsync("/api/media/posts/generate", null);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("ảnh", (await Json.Read(res)).GetProperty("error").GetString()!);
    }

    [DbFact]
    public async Task Danh_sach_bai_luc_dau_la_rong()
    {
        var page = await Json.Read(await NewUser().GetAsync("/api/media/posts"));

        Json.HasExactly(page, "total", "limit", "offset", "items");
        Assert.Equal(0, page.GetProperty("total").GetInt32());
    }
}
