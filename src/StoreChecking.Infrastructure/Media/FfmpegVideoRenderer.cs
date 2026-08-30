using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using StoreChecking.Application.Abstractions;

namespace StoreChecking.Infrastructure.Media;

/// <summary>
/// Builds the finished video with ffmpeg.
///
/// <para>Server side, not in the browser. The existing slideshow maker runs on WebCodecs in
/// the page, which is fine when someone is sitting there — but a browser cannot be open at
/// one in the morning, and the whole point here is five videos a day with nobody watching.
/// </para>
/// </summary>
public sealed class FfmpegVideoRenderer(ILogger<FfmpegVideoRenderer> log) : IVideoRenderer
{
    /// <summary>9:16 — what TikTok fills the screen with.</summary>
    private const int Width = 1080;
    private const int Height = 1920;

    /// <summary>Ceiling on one render, so a stuck ffmpeg cannot hold the daily job forever.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);

    public async Task<decimal> RenderAsync(
        IReadOnlyList<string> imagePaths, string audioPath, string outPath,
        CancellationToken ct = default)
    {
        if (imagePaths.Count == 0) throw new InvalidOperationException("Không có ảnh nào để ghép.");

        var audioSec = await ProbeDurationAsync(audioPath, ct);
        if (audioSec <= 0) throw new InvalidOperationException("Không đọc được độ dài file giọng đọc.");

        // Pictures share the spoken length evenly, so the video ends exactly when the voice
        // does. Fixing seconds-per-picture instead leaves either silence at the end or a
        // sentence cut off mid-word.
        var perImage = (double)audioSec / imagePaths.Count;

        var listFile = Path.Combine(Path.GetTempPath(), $"slides-{Guid.NewGuid():N}.txt");
        try
        {
            // concat demuxer: each picture, then how long it stays. The last one is repeated
            // without a duration because ffmpeg drops the final entry's timing otherwise and
            // the closing picture would flash past.
            var lines = new List<string>();
            foreach (var p in imagePaths)
            {
                lines.Add($"file '{Escape(p)}'");
                lines.Add($"duration {perImage.ToString("0.###", CultureInfo.InvariantCulture)}");
            }
            lines.Add($"file '{Escape(imagePaths[^1])}'");
            await File.WriteAllLinesAsync(listFile, lines, ct);

            var filter =
                // Fit the whole picture inside the frame, then pad — never crop. Cropping a
                // model car photo to 9:16 is how you cut the car in half.
                $"scale={Width}:{Height}:force_original_aspect_ratio=decrease," +
                $"pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2:color=black," +
                "format=yuv420p";

            var args = new[]
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "concat", "-safe", "0", "-i", listFile,
                "-i", audioPath,
                "-vf", filter,
                "-r", "30",
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "23",
                "-c:a", "aac", "-b:a", "128k",
                // Measured: with a 6.96s voice track the file comes out 7.50s, because the
                // concat list repeats the final picture and -shortest does not trim it back.
                // Left as is — half a second holding on the last frame reads as a beat at the
                // end, and cutting the closing line off mid-word would be the worse trade.
                "-shortest",
                "-movflags", "+faststart",
                outPath,
            };

            await RunAsync("ffmpeg", args, ct);

            // Đo lại trên chính file vừa ghép chứ không trả về độ dài giọng đọc: hai con số
            // lệch nhau (đo thật: giọng 6.96s, video 7.50s), và thứ kho video cần hiển thị
            // là độ dài của file người ta sắp tải về.
            var actual = await ProbeDurationAsync(outPath, ct);
            log.LogInformation("Ghép xong video {File} ({Sec}s, {N} ảnh).",
                Path.GetFileName(outPath), actual, imagePaths.Count);

            return actual > 0 ? actual : audioSec;
        }
        finally { try { File.Delete(listFile); } catch { /* file tạm */ } }
    }

    public async Task<string> MakeThumbAsync(string imagePath, int maxWidth, CancellationToken ct = default)
    {
        var dest = Path.Combine(Path.GetTempPath(), $"thumb-{Guid.NewGuid():N}.jpg");

        // -2 keeps the height even, which libx264 insists on and which costs nothing here.
        await RunAsync("ffmpeg", [
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", imagePath,
            "-vf", $"scale='min({maxWidth},iw)':-2",
            "-q:v", "4",
            dest,
        ], ct);

        return dest;
    }

    private async Task<decimal> ProbeDurationAsync(string path, CancellationToken ct)
    {
        var text = await RunAsync("ffprobe", [
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            path,
        ], ct);

        return decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? Math.Round(d, 2)
            : 0m;
    }

    private static async Task<string> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Không chạy được {exe}.");

        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Limit);

        try { await proc.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* đã chết */ }
            throw new InvalidOperationException($"{exe} chạy quá {Limit.TotalMinutes} phút, đã dừng.");
        }

        if (proc.ExitCode != 0)
        {
            // ffmpeg says what went wrong on stderr and nowhere else; the tail is the part
            // that names the actual problem.
            var err = await stderr;
            throw new InvalidOperationException(
                $"{exe} lỗi {proc.ExitCode}: {(err.Length <= 400 ? err : err[^400..])}");
        }

        return await stdout;
    }

    /// <summary>concat lists are single-quoted, so a quote inside a path has to be escaped.</summary>
    private static string Escape(string path) => path.Replace("'", @"'\''");
}
