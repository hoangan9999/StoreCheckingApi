using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoreChecking.Application.Abstractions;

namespace StoreChecking.Infrastructure.Media;

/// <summary>
/// Asks Gemini to look at the pictures and write what the voice will say.
///
/// <para>Same model the Vercel functions already use, and it takes images — both checked
/// against Google's own documentation rather than assumed, then proved on a real product
/// photo, which came back "Ferrari SF90 Stradale, Hot Wheels, đỏ".</para>
/// </summary>
public sealed class GeminiScriptWriter(
    IHttpClientFactory http,
    IVideoRenderer renderer,
    ILogger<GeminiScriptWriter> log,
    string apiKey,
    string model = "gemini-3.6-flash") : IScriptWriter
{
    /// <summary>
    /// How wide the pictures are shrunk to before being sent.
    /// <para>Full-size photos are about 300 KB each; fifteen of them is a 6 MB request for
    /// every video, five times a day. At 768px the model still names the car correctly and
    /// the request drops to roughly a tenth of that.</para>
    /// </summary>
    private const int ThumbWidth = 768;

    /// <summary>
    /// Góc đạo lý cho mỗi video, bốc ngẫu nhiên một cái.
    ///
    /// <para>Đây là thứ làm cho năm video trong ngày khác nhau thật sự. Cùng một lời nhắc và
    /// cùng một kho ảnh thì mô hình trả về những đoạn na ná nhau — đã xảy ra: mọi video ra
    /// gần như một nội dung. Ép mỗi lượt đi từ một góc khác nhau rẻ hơn và chắc hơn nhiều so
    /// với việc chỉ vặn temperature rồi mong nó tự nghĩ ra chuyện mới.</para>
    /// </summary>
    private static readonly string[] Angles =
    [
        "thời gian và sự kiên nhẫn — thứ đáng giá không đến vội",
        "giá trị thật không nằm ở vẻ ngoài",
        "ước mơ hồi nhỏ và cách người lớn giữ lại nó",
        "sưu tầm là giữ lại ký ức, không phải giữ đồ",
        "chậm mà chắc, đi đường dài",
        "đam mê không cần ai gật đầu mới thành thật",
        "thành công là cộng dồn những bước rất nhỏ",
        "biết đủ, và vui với thứ mình đang có",
        "chọn cái mình thích hay cái người khác trầm trồ",
        "thứ rẻ tiền với người này là báu vật với người kia",
        "cũ không có nghĩa là hết giá trị",
        "kiên trì với một thứ đủ lâu thì nó thành bản sắc",
    ];

    private const string Prompt = """
        Bạn viết lời thoại cho video TikTok về xe mô hình (diecast), có gắn giỏ hàng.

        NHÌN KỸ từng ảnh và nhận ra đó là xe gì — tên xe, hãng sản xuất, màu, chi tiết
        đáng chú ý. Nếu nhận ra thương hiệu mô hình (Hot Wheels, Tomica, Mini GT...) thì
        nói luôn.

        Bố cục BẮT BUỘC theo đúng thứ tự này:
        1. MỞ BẰNG ĐẠO LÝ (khoảng một nửa lời thoại). Một suy ngẫm đời thường, gần gũi,
           đi từ góc đã cho bên dưới. Kể như đang tâm sự, KHÔNG lên lớp, không giáo điều.
           Câu đầu phải giữ chân người xem trong 2 giây đầu.
        2. BẮC CẦU sang mấy chiếc xe trong ảnh một cách tự nhiên, gọi tên cụ thể vài mẫu.
        3. KẾT bằng câu mời bấm giỏ hàng, nhẹ nhàng, không sáo rỗng.

        Yêu cầu chung:
        - Tiếng Việt, 70-100 từ, một đoạn liền mạch để đọc lên. (Tiếng Việt đọc lên khoảng
          ba từ một giây, nên chừng đó rơi vào 25-35 giây — đủ dài để nói được điều gì đó,
          đủ ngắn cho TikTok.)
        - Giọng ấm và cuốn, có chút hóm hỉnh — không phải giọng quảng cáo khô khan.
        - KHÔNG bịa giá. KHÔNG emoji. KHÔNG xuống dòng. KHÔNG nói ra chữ "đạo lý".
        - KHÔNG mở đầu bằng "Bạn có biết", "Có bao giờ bạn", "Trong cuộc sống".

        Kèm một tiêu đề ngắn dưới 60 ký tự để đặt tên video.
        """;

    public async Task<VideoScript> WriteAsync(
        IReadOnlyList<string> imagePaths, CancellationToken ct = default)
    {
        if (imagePaths.Count == 0) throw new InvalidOperationException("Không có ảnh nào để viết kịch bản.");

        var angle = Angles[Random.Shared.Next(Angles.Length)];
        var parts = new List<object>
        {
            new { text = Prompt + "\n\nGÓC ĐẠO LÝ CHO VIDEO NÀY: " + angle },
        };
        var thumbs = new List<string>();

        try
        {
            foreach (var path in imagePaths)
            {
                var thumb = await renderer.MakeThumbAsync(path, ThumbWidth, ct);
                thumbs.Add(thumb);
                parts.Add(new
                {
                    inline_data = new
                    {
                        mime_type = "image/jpeg",
                        data = Convert.ToBase64String(await File.ReadAllBytesAsync(thumb, ct)),
                    },
                });
            }

            var body = new
            {
                contents = new[] { new { parts } },
                // Structured output rather than parsing prose. Asking for JSON in the prompt
                // and hoping is how you end up with a "title" wrapped in ```json fences on
                // the one run nobody was watching.
                generationConfig = new
                {
                    // Cao hơn mặc định: cùng lời nhắc và cùng kho ảnh thì mô hình trả về
                    // những đoạn rất giống nhau. Góc đạo lý ở trên lo phần khác nhau về nội
                    // dung, còn con số này lo phần khác nhau về cách diễn đạt.
                    temperature = 1.15,
                    responseMimeType = "application/json",
                    responseSchema = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            title = new { type = "STRING" },
                            script = new { type = "STRING" },
                        },
                        required = new[] { "title", "script" },
                    },
                },
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            using var client = http.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(3);

            using var res = await client.PostAsJsonAsync(url, body, ct);
            var text = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                // The key is in the URL, so the URL must never reach a log.
                throw new InvalidOperationException(
                    $"Gemini trả {(int)res.StatusCode}: {Trim(text)}");
            }

            return Parse(text);
        }
        finally
        {
            foreach (var t in thumbs) { try { File.Delete(t); } catch { /* ảnh tạm, kệ */ } }
        }
    }

    private VideoScript Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);

        var inner = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";

        using var payload = JsonDocument.Parse(inner);
        var title = payload.RootElement.GetProperty("title").GetString()?.Trim() ?? "";
        var script = payload.RootElement.GetProperty("script").GetString()?.Trim() ?? "";

        if (script.Length == 0) throw new InvalidOperationException("Gemini trả kịch bản rỗng.");

        log.LogInformation("Kịch bản: \"{Title}\" — {Words} từ.", title, script.Split(' ').Length);
        return new VideoScript(title, script);
    }

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
