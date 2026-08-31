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

        Json.HasExactly(s, "autoPost", "fanpageReady");

        // Người dùng yêu cầu bật sẵn. Chưa ai đụng tới cài đặt thì vẫn phải là true.
        Assert.True(s.GetProperty("autoPost").GetBoolean());

        // Máy chủ chạy test không khai khoá Facebook, nên phải báo là chưa sẵn sàng — đó là
        // thứ giao diện dựa vào để nói rõ vì sao bật mà không có bài nào lên.
        Assert.False(s.GetProperty("fanpageReady").GetBoolean());
    }

    [DbFact]
    public async Task Tat_roi_bat_lai_thi_nho_dung_trang_thai()
    {
        var c = NewUser();

        var off = await Json.Read(await c.PutJson("/api/media/settings/auto-post", new { autoPost = false }));
        Assert.False(off.GetProperty("autoPost").GetBoolean());

        // Đọc lại từ database, không phải tin vào câu trả lời của lần ghi.
        var again = await Json.Read(await c.GetAsync("/api/media/settings"));
        Assert.False(again.GetProperty("autoPost").GetBoolean());

        var on = await Json.Read(await c.PutJson("/api/media/settings/auto-post", new { autoPost = true }));
        Assert.True(on.GetProperty("autoPost").GetBoolean());

        // Bật rồi tắt rồi bật lại không được đẻ ra dòng thứ hai — khoá chính là (user, key),
        // nếu sai thì lần ghi thứ hai sẽ ném lỗi trùng khoá chứ không im lặng.
        Assert.Equal(HttpStatusCode.OK,
            (await c.PutJson("/api/media/settings/auto-post", new { autoPost = true })).StatusCode);
    }

    // Cài đặt của người này KHÔNG được rò sang người kia. Sai chỗ này nghĩa là video của
    // một người tự lên Fanpage vì người khác bật công tắc.
    [DbFact]
    public async Task Cai_dat_cua_ai_nguoi_nay_giu()
    {
        var a = NewUser();
        var b = NewUser();

        await a.PutJson("/api/media/settings/auto-post", new { autoPost = false });

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
}
