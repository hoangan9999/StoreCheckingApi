using System.Text.Json;

namespace StoreChecking.Api.Models;

/// <summary>
/// A saved vocabulary word together with everything the AI generated for it.
/// </summary>
public class EnglishWord
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    public string Word { get; set; } = "";

    /// <summary>
    /// The full AI result (meaning, part of speech, one example per tense), stored as
    /// jsonb so a saved word can be reviewed without calling the AI again.
    /// <para>Kept as raw JSON rather than a typed shape on purpose: the generator's
    /// output format belongs to the Angular client, and the API has no reason to care
    /// about it or to break when it changes.</para>
    /// </summary>
    public JsonDocument Data { get; set; } = JsonDocument.Parse("{}");

    public DateTimeOffset CreatedAt { get; set; }
}
