namespace StoreChecking.Application.Abstractions;

/// <summary>What the AI came back with for one video.</summary>
public record VideoScript(string Title, string Script);

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
