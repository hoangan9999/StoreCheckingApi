using Microsoft.Extensions.Logging;
using StoreChecking.Application.Abstractions;

namespace StoreChecking.Infrastructure.Media;

/// <summary>
/// Picture and video files on disk, under the `media` volume.
///
/// <para>Every path decision lives here and nowhere else. A filename that came from outside
/// must never be able to reach `../../etc/passwd`, and one place to be sure of beats the
/// same check copied into every caller — where the copy that gets forgotten is the one that
/// matters.</para>
/// </summary>
public sealed class DiskMediaStorage : IMediaStorage
{
    private readonly string _images;
    private readonly string _videos;
    private readonly string _notes;

    public DiskMediaStorage(string root, ILogger<DiskMediaStorage> log)
    {
        _images = Path.Combine(root, "images");
        _videos = Path.Combine(root, "videos");
        // Thư mục riêng, KHÔNG phải `images`: kho ảnh đó là nguồn để dựng video bán hàng,
        // ảnh ghi chú lọt vào đó là lên sóng ảnh chụp màn hình.
        _notes = Path.Combine(root, "notes");
        Directory.CreateDirectory(_images);
        Directory.CreateDirectory(_videos);
        Directory.CreateDirectory(_notes);
        log.LogInformation("Kho ảnh/video: {Root}", root);
    }

    public Task<string> SaveImageAsync(Stream content, string extension, CancellationToken ct = default) =>
        SaveAsync(_images, content, extension, ct);

    public Task<string> SaveVideoAsync(Stream content, string extension, CancellationToken ct = default) =>
        SaveAsync(_videos, content, extension, ct);

    public Stream? OpenImage(string filename) => Open(_images, filename);
    public Stream? OpenVideo(string filename) => Open(_videos, filename);

    public string? ImagePath(string filename)
    {
        var p = Resolve(_images, filename);
        return p is not null && File.Exists(p) ? p : null;
    }

    /// <summary>Where a video will be written. The file does not exist yet, so no check.</summary>
    public string VideoPath(string filename) =>
        Resolve(_videos, filename) ?? throw new ArgumentException("Tên file không hợp lệ.", nameof(filename));

    public void DeleteImage(string filename) => Delete(_images, filename);
    public void DeleteVideo(string filename) => Delete(_videos, filename);

    public Task<string> SaveNoteImageAsync(Stream content, string extension, CancellationToken ct = default) =>
        SaveAsync(_notes, content, extension, ct);

    public Stream? OpenNoteImage(string filename) => Open(_notes, filename);
    public void DeleteNoteImage(string filename) => Delete(_notes, filename);

    public IReadOnlyList<string> ListVideoFiles()
    {
        try { return Directory.GetFiles(_videos).Select(Path.GetFileName).OfType<string>().ToList(); }
        catch { return []; }
    }

    private static async Task<string> SaveAsync(
        string dir, Stream content, string extension, CancellationToken ct)
    {
        // The name is generated, never taken from the upload. A caller cannot choose where
        // its bytes land, so a hostile name has nothing to act on.
        var ext = SafeExtension(extension);
        var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{ext}";

        // Written aside and renamed: a rename within one directory is atomic, so a crash
        // half way leaves a stray .part rather than a truncated file that looks complete.
        var final = Path.Combine(dir, name);
        var temp = final + ".part";

        try
        {
            await using (var fs = File.Create(temp)) await content.CopyToAsync(fs, ct);
            File.Move(temp, final);
            return name;
        }
        catch
        {
            try { File.Delete(temp); } catch { /* dọn dẹp, hỏng cũng không sao */ }
            throw;
        }
    }

    private static Stream? Open(string dir, string filename)
    {
        var p = Resolve(dir, filename);
        if (p is null || !File.Exists(p)) return null;
        return File.OpenRead(p);
    }

    private static void Delete(string dir, string filename)
    {
        var p = Resolve(dir, filename);
        if (p is not null) { try { File.Delete(p); } catch { /* mất rồi thì thôi */ } }
    }

    /// <summary>
    /// Turns a stored name into a full path, or null if it tries to leave the directory.
    /// <para>Checked on the RESOLVED path, not by looking for ".." in the text: encodings,
    /// symlinks and absolute paths all get past a string check, but none of them survive
    /// asking the filesystem where the path actually ends up.</para>
    /// </summary>
    private static string? Resolve(string dir, string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return null;

        var full = Path.GetFullPath(Path.Combine(dir, filename));
        var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;

        return full.StartsWith(root, StringComparison.Ordinal) ? full : null;
    }

    private static string SafeExtension(string extension)
    {
        var ext = (extension ?? "").Trim().ToLowerInvariant();
        if (!ext.StartsWith('.')) ext = '.' + ext;
        return ext.Length is > 1 and <= 6 && ext[1..].All(char.IsLetterOrDigit) ? ext : ".bin";
    }
}
