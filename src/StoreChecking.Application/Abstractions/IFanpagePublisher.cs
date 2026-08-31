namespace StoreChecking.Application.Abstractions;

/// <summary>Result of putting a video on the Fanpage.</summary>
/// <param name="PostId">Facebook's id for it, so the post can be opened and checked.</param>
public record FanpagePost(string PostId);

/// <summary>
/// Publishes a finished video to the shop's Facebook page.
///
/// <para>Behind an interface so the Application layer never sees Graph API, an access token
/// or an HttpClient — and so a test can assert what would be posted without posting it.</para>
///
/// <para><see cref="Configured"/> exists because the credentials are optional: the API runs
/// perfectly well with no Facebook set up, and in that case video generation must carry on
/// silently rather than record a failure on every single video.</para>
/// </summary>
public interface IFanpagePublisher
{
    /// <summary>False when no page id or token is configured; nothing will be posted.</summary>
    bool Configured { get; }

    /// <summary>
    /// Uploads one video file with its caption.
    /// <para>Throws with Facebook's own message when the upload is refused — the caller
    /// records it against the video rather than treating it as a broken render.</para>
    /// </summary>
    Task<FanpagePost> PostVideoAsync(
        string filePath, string title, string caption, CancellationToken ct = default);

    /// <summary>
    /// The caption for a video: what the AI wrote, then the order link and an invitation
    /// to message the page. Deliberately WITHOUT a price — a video covers ten to fifteen
    /// different cars, so any single number on it would be wrong.
    /// </summary>
    string BuildCaption(string script);
}
