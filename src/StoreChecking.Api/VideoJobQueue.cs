using System.Threading.Channels;

namespace StoreChecking.Api;

/// <summary>
/// Chỗ đặt yêu cầu "dựng ngay" cho tiến trình nền nhận.
///
/// <para>Dựng năm video mất chừng năm phút. Một request HTTP không chờ nổi quãng đó — client
/// của app tự bỏ cuộc sau ba mươi giây — nên nút bấm chỉ ĐẶT yêu cầu rồi trả lời ngay, còn
/// việc nặng chạy ở tiến trình nền. Giao diện theo dõi tiến độ qua cột `status` của từng
/// dòng video, vốn đã ghi rõ đang ở chặng nào.</para>
///
/// <para>Hàng đợi có giới hạn và bỏ qua yêu cầu mới khi đầy: bấm nút mười lần liên tiếp
/// không được phép xếp hàng ra mười mẻ video.</para>
/// </summary>
public sealed class VideoJobQueue
{
    private readonly Channel<int> _channel =
        Channel.CreateBounded<int>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    /// <summary>Đặt một mẻ. Trả về false khi hàng đợi đang đầy.</summary>
    public bool Request(int count) => _channel.Writer.TryWrite(count);

    public ChannelReader<int> Reader => _channel.Reader;
}
