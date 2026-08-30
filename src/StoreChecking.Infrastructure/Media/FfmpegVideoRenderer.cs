using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using StoreChecking.Application.Abstractions;

namespace StoreChecking.Infrastructure.Media;

/// <summary>
/// Builds the finished video with ffmpeg.
///
/// <para>Server side, not in the browser. The existing slideshow maker runs on WebCodecs in
/// the page, which is fine when someone is sitting there — but a browser cannot be open at
/// seven in the morning, and the whole point here is videos appearing on their own.</para>
///
/// <para>Three things separate this from a slideshow, and all three are why the filter graph
/// is built per image instead of handed to the concat demuxer:</para>
/// <list type="number">
/// <item>the picture itself, blurred and enlarged, fills the frame behind — never black bars;</item>
/// <item>every picture drifts and zooms slowly, because a still frame is where people scroll past;</item>
/// <item>pictures dissolve into each other rather than cutting.</item>
/// </list>
/// </summary>
public sealed class FfmpegVideoRenderer(ILogger<FfmpegVideoRenderer> log) : IVideoRenderer
{
    /// <summary>9:16 — what TikTok fills the screen with.</summary>
    private const int Width = 1080;
    private const int Height = 1920;
    private const int Fps = 30;

    /// <summary>
    /// How long one picture dissolves into the next.
    /// <para>Short on purpose. Half a second reads as a cut with softened edges; a second and
    /// a half reads as a wedding slideshow.</para>
    /// </summary>
    private const double Fade = 0.5;

    /// <summary>
    /// How far the slow zoom travels, and how much bigger the frame is rendered before it.
    /// <para>zoompan crops from the frame it is given, so a 1080-wide source only has 1080
    /// pixels to work with and the drift comes out visibly steppy. Composing at 1.5x first
    /// gives it room to move, which is the accepted fix.</para>
    /// </summary>
    private const double ZoomTo = 1.12;
    private const int Over = 3;      // 1.5x, written as 3/2 to keep the numbers even

    /// <summary>Ceiling on one render, so a stuck ffmpeg cannot hold the daily job forever.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(15);

    public async Task<decimal> RenderAsync(
        IReadOnlyList<string> imagePaths, string audioPath, string outPath,
        CancellationToken ct = default)
    {
        if (imagePaths.Count == 0) throw new InvalidOperationException("Không có ảnh nào để ghép.");

        var audioSec = (double)await ProbeDurationAsync(audioPath, ct);
        if (audioSec <= 0) throw new InvalidOperationException("Không đọc được độ dài file giọng đọc.");

        var n = imagePaths.Count;

        // Every dissolve eats `Fade` seconds of the running time, because two pictures share
        // it. Adding that back is what makes the finished video match the voice instead of
        // ending several seconds early.
        var per = (audioSec + (n - 1) * Fade) / n;

        // A dissolve cannot last longer than the pictures it joins.
        if (per <= Fade * 1.5)
            return await RenderPlainAsync(imagePaths, audioPath, outPath, audioSec, ct);

        var (filter, lastLabel) = BuildFilter(n, per);

        var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };
        foreach (var p in imagePaths) { args.Add("-i"); args.Add(p); }
        args.Add("-i"); args.Add(audioPath);

        args.AddRange([
            "-filter_complex", filter,
            "-map", lastLabel,
            "-map", $"{n}:a",
            "-r", Fps.ToString(),
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "22", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "128k",
            "-shortest",
            "-movflags", "+faststart",
            outPath,
        ]);

        await RunAsync("ffmpeg", [.. args], ct);

        var actual = await ProbeDurationAsync(outPath, ct);
        log.LogInformation("Ghép xong video {File} ({Sec}s, {N} ảnh, có trôi ảnh và chuyển cảnh).",
            Path.GetFileName(outPath), actual, n);

        return actual > 0 ? actual : (decimal)audioSec;
    }

    /// <summary>
    /// The filter graph: background, foreground, drift, then dissolve them together.
    /// </summary>
    private static (string Filter, string Last) BuildFilter(int n, double per)
    {
        var sb = new StringBuilder();
        var frames = (int)Math.Ceiling(per * Fps);

        var bigW = Width * Over / 2;
        var bigH = Height * Over / 2;

        for (var i = 0; i < n; i++)
        {
            // Background: the same picture, blown up to cover the frame and blurred. Anything
            // else here is a black bar, which is the giveaway that a landscape photo was
            // dropped into a portrait video.
            sb.Append(CultureInfo.InvariantCulture,
                $"[{i}:v]scale={bigW}:{bigH}:force_original_aspect_ratio=increase," +
                $"crop={bigW}:{bigH},boxblur=24:2,setsar=1[bg{i}];");

            // Foreground: the whole picture, fitted inside. Never cropped — cropping a model
            // car to portrait is how you cut the car in half.
            sb.Append(CultureInfo.InvariantCulture,
                $"[{i}:v]scale={bigW}:{bigH}:force_original_aspect_ratio=decrease,setsar=1[fg{i}];");

            sb.Append(CultureInfo.InvariantCulture,
                $"[bg{i}][fg{i}]overlay=(W-w)/2:(H-h)/2[c{i}];");

            // Drift. Odd pictures zoom in from the centre, even ones zoom out while sliding —
            // alternating keeps a run of fifteen from feeling mechanical.
            var step = (ZoomTo - 1.0) / frames;
            var z = i % 2 == 0
                ? $"min(zoom+{step.ToString("0.######", CultureInfo.InvariantCulture)},{ZoomTo.ToString("0.##", CultureInfo.InvariantCulture)})"
                : $"max({ZoomTo.ToString("0.##", CultureInfo.InvariantCulture)}-on*{step.ToString("0.######", CultureInfo.InvariantCulture)},1.0)";

            var x = (i % 4) switch
            {
                0 => "iw/2-(iw/zoom/2)",
                1 => "(iw-iw/zoom)*on/" + frames,
                2 => "iw/2-(iw/zoom/2)",
                _ => "(iw-iw/zoom)*(1-on/" + frames + ")",
            };

            sb.Append(CultureInfo.InvariantCulture,
                $"[c{i}]zoompan=z='{z}':x='{x}':y='ih/2-(ih/zoom/2)':" +
                $"d={frames}:s={Width}x{Height}:fps={Fps}[z{i}];");
        }

        if (n == 1) return (sb.ToString().TrimEnd(';'), "[z0]");

        // Dissolves, chained. Each one starts `Fade` before the running total, which is also
        // why the running total grows by (per - Fade) rather than by per.
        var acc = per;
        var cur = "[z0]";

        for (var i = 1; i < n; i++)
        {
            var offset = acc - Fade;
            var next = i == n - 1 ? "[vout]" : $"[x{i}]";

            sb.Append(CultureInfo.InvariantCulture,
                $"{cur}[z{i}]xfade=transition=fade:duration={Fade.ToString("0.##", CultureInfo.InvariantCulture)}:" +
                $"offset={offset.ToString("0.###", CultureInfo.InvariantCulture)}{next};");

            cur = next;
            acc += per - Fade;
        }

        return (sb.ToString().TrimEnd(';'), "[vout]");
    }

    /// <summary>
    /// Fallback for a voice track so short the pictures cannot dissolve into each other.
    /// <para>Better a plain cut than a dissolve longer than the picture it joins, which
    /// ffmpeg refuses outright.</para>
    /// </summary>
    private async Task<decimal> RenderPlainAsync(
        IReadOnlyList<string> imagePaths, string audioPath, string outPath, double audioSec,
        CancellationToken ct)
    {
        var listFile = Path.Combine(Path.GetTempPath(), $"slides-{Guid.NewGuid():N}.txt");
        try
        {
            var per = audioSec / imagePaths.Count;
            var lines = new List<string>();
            foreach (var p in imagePaths)
            {
                lines.Add($"file '{Escape(p)}'");
                lines.Add($"duration {per.ToString("0.###", CultureInfo.InvariantCulture)}");
            }
            lines.Add($"file '{Escape(imagePaths[^1])}'");
            await File.WriteAllLinesAsync(listFile, lines, ct);

            await RunAsync("ffmpeg", [
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "concat", "-safe", "0", "-i", listFile,
                "-i", audioPath,
                "-vf", $"scale={Width}:{Height}:force_original_aspect_ratio=decrease," +
                       $"pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2:color=black,format=yuv420p",
                "-r", Fps.ToString(),
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "23",
                "-c:a", "aac", "-b:a", "128k",
                "-shortest", "-movflags", "+faststart", outPath,
            ], ct);

            log.LogInformation("Ghép xong video {File} ({Sec}s) — bản đơn giản, giọng đọc quá ngắn.",
                Path.GetFileName(outPath), audioSec);

            var actual = await ProbeDurationAsync(outPath, ct);
            return actual > 0 ? actual : (decimal)audioSec;
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
