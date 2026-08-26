namespace StoreChecking.Api.Models;

/// <summary>
/// One English sentence kept from a speaking practice session, for later review.
/// </summary>
public class SavedSentence
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    public string Text { get; set; } = "";

    /// <summary>Where the sentence came from — "câu trả lời mẫu", "cách nói tự nhiên hơn"…</summary>
    public string Note { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
