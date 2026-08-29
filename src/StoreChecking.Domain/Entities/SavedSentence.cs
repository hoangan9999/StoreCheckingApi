using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>
/// One English sentence kept from a speaking practice session, for later review.
/// </summary>
public class SavedSentence : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    public string Text { get; set; } = "";

    /// <summary>Where the sentence came from — "câu trả lời mẫu", "cách nói tự nhiên hơn"…</summary>
    public string Note { get; set; } = "";

    /// <summary>
    /// The line that came before this one in the conversation, usually the question it
    /// answers. Empty when there was none, or for rows saved before this was kept.
    /// <para>Without it a saved answer is unreadable weeks later: "Yes, for about three
    /// years" means nothing on its own.</para>
    /// </summary>
    public string Context { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
