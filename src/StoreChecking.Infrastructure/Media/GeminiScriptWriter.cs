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
    /// Roughly what fits the target length.
    /// <para>Vietnamese read aloud runs near three words a second, so 60-90 words lands
    /// around 25-30 seconds — long enough to say something, short enough for TikTok.</para>
    /// </summary>
    private const string Prompt = """
        Bạn viết lời thoại cho video TikTok bán xe mô hình (diecast).

        NHÌN KỸ từng ảnh và nhận ra đó là xe gì — tên xe, hãng sản xuất, màu, chi tiết
        đáng chú ý. Nếu nhận ra thương hiệu mô hình (Hot Wheels, Tomica, Mini GT...) thì
        nói luôn.

        Viết một đoạn lời thoại DUY NHẤT, liền mạch, để đọc lên trong video:
        - Tiếng Việt, 60-90 từ.
        - Giọng VUI, hào hứng, cuốn người xem — không phải giọng quảng cáo khô khan.
        - Câu đầu phải giữ chân người xem trong 2 giây đầu.
        - Nhắc tên vài mẫu xe cụ thể nhìn thấy trong ảnh.
        - Kết bằng câu mời bấm giỏ hàng, tự nhiên, không sáo rỗng.
        - KHÔNG bịa giá. KHÔNG dùng emoji. KHÔNG xuống dòng.

        Kèm một tiêu đề ngắn dưới 60 ký tự để đặt tên video.
        """;

    public async Task<VideoScript> WriteAsync(
        IReadOnlyList<string> imagePaths, CancellationToken ct = default)
    {
        if (imagePaths.Count == 0) throw new InvalidOperationException("Không có ảnh nào để viết kịch bản.");

        var parts = new List<object> { new { text = Prompt } };
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
