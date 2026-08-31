using Microsoft.Extensions.Logging.Abstractions;
using StoreChecking.Infrastructure.Media;

namespace StoreChecking.ContractTests;

/// <summary>
/// Caption đăng kèm video lên Fanpage.
///
/// <para>Không cần database nên chạy được ở mọi máy. Đáng có test riêng vì đây là thứ
/// KHÁCH HÀNG đọc: sai thì không ai báo lỗi, chỉ có bài đăng ghi nhầm.</para>
/// </summary>
public class FanpageCaptionTests
{
    /// <summary>HttpClient không bao giờ được đụng tới trong các test này.</summary>
    private static FacebookPublisher Publisher(FacebookOptions o) =>
        new(null!, NullLogger<FacebookPublisher>.Instance, o);

    [Fact]
    public void Caption_co_link_dat_hang_va_loi_moi_inbox()
    {
        var text = Publisher(new FacebookOptions { OrderLink = "https://shop.test/order" })
            .BuildCaption("Xe đẹp lắm nha.");

        Assert.Contains("Xe đẹp lắm nha.", text);
        Assert.Contains("https://shop.test/order", text);
        Assert.Contains("Inbox", text);
    }

    // Yêu cầu rõ ràng: video KHÔNG để giá. Một video khoe mười tới mười lăm chiếc xe khác
    // nhau, nên bất kỳ con số nào in lên đó cũng sai với phần lớn số xe trong đó.
    [Fact]
    public void Caption_khong_bao_gio_co_gia()
    {
        var text = Publisher(new FacebookOptions { OrderLink = "https://shop.test/order" })
            .BuildCaption("Chiếc này giá trị sưu tầm cao.");

        Assert.DoesNotContain("Giá", text);
        Assert.DoesNotContain("VNĐ", text);
        Assert.DoesNotContain("000", text);
    }

    [Fact]
    public void Khong_khai_link_thi_bo_dong_do_chu_khong_de_trong()
    {
        var text = Publisher(new FacebookOptions()).BuildCaption("Nội dung.");

        Assert.DoesNotContain("Đặt hàng", text);
        Assert.Contains("Inbox", text);
    }

    [Fact]
    public void Thieu_khoa_thi_Configured_false_va_khong_dang_gi()
    {
        Assert.False(Publisher(new FacebookOptions()).Configured);
        Assert.False(Publisher(new FacebookOptions { PageId = "123" }).Configured);
        Assert.True(Publisher(new FacebookOptions { PageId = "123", AccessToken = "t" }).Configured);
    }

    [Fact]
    public async Task Chua_khai_khoa_thi_doi_dang_se_bao_ngay_chu_khong_goi_Facebook()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Publisher(new FacebookOptions()).PostVideoAsync("khong-ton-tai.mp4", "t", "c"));

        Assert.Contains("FB_PAGE_ID", ex.Message);
    }
}
