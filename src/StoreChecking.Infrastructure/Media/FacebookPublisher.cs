using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoreChecking.Application.Abstractions;

namespace StoreChecking.Infrastructure.Media;

/// <summary>Everything needed to put a video on the Fanpage. All of it optional.</summary>
public sealed class FacebookOptions
{
    public string? PageId { get; set; }
    public string? AccessToken { get; set; }

    /// <summary>
    /// Post finished videos without being asked. Off by default.
    /// <para>Five videos a day onto one page is a lot: Facebook lowers the reach of a page
    /// that posts that heavily. Choosing which ones go up is a decision worth keeping with
    /// the person, so the button is the normal way and this is the exception.</para>
    /// </summary>
    public bool AutoPost { get; set; }

    /// <summary>Where people order. Goes in every caption.</summary>
    public string? OrderLink { get; set; }

    public string? Hashtags { get; set; }

    /// <summary>Graph API version. Bump it deliberately, never let it float.</summary>
    public string ApiVersion { get; set; } = "v23.0";
}

/// <summary>
/// Uploads finished videos to the shop's Facebook page.
///
/// <para>The file is sent as multipart to graph-video.facebook.com rather than handing
/// Facebook a URL to fetch. A URL would have to be one Facebook can reach, and the only
/// public address this API has is behind a Tailscale Funnel that requires a token — which
/// Facebook has no way to present. Uploading the bytes sidesteps the whole question, and
/// these videos are only a few megabytes.</para>
///
/// <para>Every caption ends the same way — order link, then an invitation to message the
/// page — and never carries a price: one video shows ten to fifteen different cars, so any
/// single number printed on it would be wrong for most of them.</para>
/// </summary>
public sealed class FacebookPublisher(
    IHttpClientFactory http,
    ILogger<FacebookPublisher> log,
    FacebookOptions options) : IFanpagePublisher
{
    private const string DefaultHashtags = "#hotwheels #diecast #xemohinh164";

    public bool Configured =>
        !string.IsNullOrWhiteSpace(options.PageId) &&
        !string.IsNullOrWhiteSpace(options.AccessToken);

    public bool AutoPost => options.AutoPost;

    public string BuildCaption(string script)
    {
        var lines = new List<string> { script.Trim() };

        if (!string.IsNullOrWhiteSpace(options.OrderLink))
        {
            lines.Add("");
            lines.Add($"🛒 Đặt hàng: {options.OrderLink.Trim()}");
        }

        lines.Add("📩 Inbox để hỏi chi tiết nhé!");

        var tags = string.IsNullOrWhiteSpace(options.Hashtags) ? DefaultHashtags : options.Hashtags.Trim();
        if (tags.Length > 0)
        {
            lines.Add("");
            lines.Add(tags);
        }

        return string.Join("\n", lines);
    }

    public async Task<FanpagePost> PostVideoAsync(
        string filePath, string title, string caption, CancellationToken ct = default)
    {
        if (!Configured)
            throw new InvalidOperationException(
                "Chưa khai FB_PAGE_ID / FB_PAGE_ACCESS_TOKEN, không đăng lên Fanpage được.");

        if (!File.Exists(filePath))
            throw new InvalidOperationException("Không tìm thấy file video để đăng.");

        using var client = http.CreateClient();
        // Uploading a few megabytes over a home connection, and Facebook only answers once
        // it has taken the whole file.
        client.Timeout = TimeSpan.FromMinutes(10);

        using var form = new MultipartFormDataContent();
        await using var file = File.OpenRead(filePath);

        var video = new StreamContent(file);
        video.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
        form.Add(video, "source", Path.GetFileName(filePath));

        form.Add(new StringContent(title), "title");
        form.Add(new StringContent(caption), "description");
        form.Add(new StringContent(options.AccessToken!), "access_token");

        var url = $"https://graph-video.facebook.com/{options.ApiVersion}/" +
                  $"{Uri.EscapeDataString(options.PageId!)}/videos";

        using var res = await client.PostAsync(url, form, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        // Facebook answers 200 with an `error` object often enough that the status code
        // alone cannot be trusted; the body is what actually says whether it worked.
        var id = ReadId(body, out var error);

        if (!res.IsSuccessStatusCode || error is not null || id is null)
        {
            var why = error ?? (body.Length <= 300 ? body : body[..300]);
            log.LogWarning("Đăng video lên Fanpage hỏng ({Status}): {Why}", (int)res.StatusCode, why);
            throw new InvalidOperationException($"Facebook từ chối: {why}");
        }

        log.LogInformation("Đã đăng video lên Fanpage, id {PostId}", id);
        return new FanpagePost(id);
    }

    /// <summary>Pulls the new post's id out of the reply, or the reason it was refused.</summary>
    private static string? ReadId(string body, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var e))
            {
                error = e.TryGetProperty("message", out var m) ? m.GetString() : e.ToString();
                return null;
            }

            return root.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            error = body.Length <= 300 ? body : body[..300];
            return null;
        }
    }
}
