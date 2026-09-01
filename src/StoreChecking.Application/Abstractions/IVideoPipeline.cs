namespace StoreChecking.Application.Abstractions;

/// <summary>What the AI came back with for one video.</summary>
public record VideoScript(string Title, string Script);

/// <summary>Nội dung AI viết cho một bài đăng Fanpage, từ đúng một tấm ảnh.</summary>
public record PostContent(string Title, string Content);

/// <summary>
/// Looks at the pictures and writes what the voice will say.
///
/// <para>The pictures go to the model as images, not as filenames: the whole point is that
/// it works out which car each one is. Verified on a real product photo before any of this
/// was built — it answered "Ferrari SF90 Stradale, Hot Wheels, đỏ".</para>
/// </summary>
public interface IScriptWriter
{
    Task<VideoScript> WriteAsync(IReadOnlyList<string> imagePaths, CancellationToken ct = default);

    /// <summary>
    /// Viết nội dung cho NHIỀU bài đăng trong MỘT lượt gọi — mỗi ảnh một bài.
    /// </summary>
    /// <remarks>
    /// Gộp cả mẻ vào một lượt là có chủ đích, không phải để chạy nhanh. Hạn mức Gemini của
    /// tài khoản này bị chặn ở SỐ LƯỢT GỌI (20 mỗi ngày) chứ không phải dung lượng — đo
    /// ngày 2026-08-30, token mỗi phút mới dùng chưa tới 1%. Gọi riêng từng bài sẽ ngốn 5
    /// lượt cho việc mà một lượt làm xong, và 5 lượt đó lấy đi từ phần của tab tiếng Anh.
    /// <para>Trả về đúng thứ tự của <paramref name="imagePaths"/>, và đúng bấy nhiêu phần
    /// tử — bên gọi ghép lại theo vị trí.</para>
    /// </remarks>
    Task<IReadOnlyList<PostContent>> WritePostsAsync(
        IReadOnlyList<string> imagePaths, CancellationToken ct = default);
}

/// <summary>Reads the script aloud and hands back audio.</summary>
public interface IVoiceSynthesizer
{
    /// <summary>Writes spoken audio to <paramref name="destPath"/>.</summary>
    Task SpeakToFileAsync(string text, string destPath, CancellationToken ct = default);
}

/// <summary>Turns pictures plus audio into a finished vertical video.</summary>
public interface IVideoRenderer
{
    /// <summary>Builds the video and returns how long it runs.</summary>
    Task<decimal> RenderAsync(
        IReadOnlyList<string> imagePaths, string audioPath, string outPath,
        CancellationToken ct = default);

    /// <summary>Shrinks a picture for sending to the AI, returning the new file's path.</summary>
    Task<string> MakeThumbAsync(string imagePath, int maxWidth, CancellationToken ct = default);
}
