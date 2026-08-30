using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoreChecking.Application.Abstractions;

namespace StoreChecking.Infrastructure.Media;

/// <summary>
/// The Adam voice, read by the Python service running on the host machine.
///
/// <para>It lives outside Docker, started at logon, so a container reaches it through
/// host.docker.internal — checked by POSTing from inside a throwaway container before any
/// of this was written, which answered 200.</para>
/// </summary>
public sealed class AdamVoice(
    IHttpClientFactory http,
    ILogger<AdamVoice> log,
    string endpoint) : IVoiceSynthesizer
{
    public async Task SpeakToFileAsync(string text, string destPath, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { text });

        using var client = http.CreateClient();
        // Ninety seconds: reading ninety words takes well under that, but the first call
        // after an idle spell also loads the model.
        client.Timeout = TimeSpan.FromSeconds(90);

        // Deliberately NOT application/json. The Python service does not answer OPTIONS, and
        // that content type is what makes a browser send a preflight — the app already found
        // this out and nas.service.ts carries the same note. Kept identical here so both
        // callers behave the same way against the same service.
        using var content = new StringContent(body, Encoding.UTF8, "text/plain");
        using var res = await client.PostAsync(endpoint, content, ct);

        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Giọng Adam trả {(int)res.StatusCode}: {(err.Length <= 200 ? err : err[..200])}");
        }

        await using (var fs = File.Create(destPath))
            await res.Content.CopyToAsync(fs, ct);

        var bytes = new FileInfo(destPath).Length;
        if (bytes == 0)
        {
            File.Delete(destPath);
            throw new InvalidOperationException("Giọng Adam trả file rỗng.");
        }

        log.LogInformation("Giọng đọc xong: {Kb} KB.", bytes / 1024);
    }
}
