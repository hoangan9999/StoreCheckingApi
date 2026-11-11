using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>
/// One lesson: a titled conversation plus the phrases worth keeping from it.
/// <para>The content is written elsewhere — an AI conversation — and pasted in whole. What
/// gets stored is the PARSED result, two columns apart, because that is the only part with
/// any value: a lesson with its English and Vietnamese separated can hide one side and
/// become something to practise against. Stored as one block of text it would be a file
/// the app merely holds.</para>
/// </summary>
public class EnglishTopic : IOwnedByUser
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public string Title { get; set; } = "";

    /// <summary>
    /// Where it sits in the user's own ordering. A new topic takes <c>min(position) - 1</c>
    /// so it lands at the top, the same way notes do.
    /// </summary>
    public int Position { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One row of a lesson: a line of the conversation, or an entry in its phrase table.
/// <para>Both in one entity because they really are the same shape — an English side, a
/// Vietnamese side, and a place in the lesson. The only difference is that a conversation
/// line has someone saying it. Splitting them would double the table, the editing path and
/// the ordering, to buy one column.</para>
/// </summary>
public class EnglishLessonLine : IOwnedByUser
{
    /// <summary>A line of the conversation.</summary>
    public const string Line = "line";

    /// <summary>A row of the "Cụm từ hay" table.</summary>
    public const string Phrase = "phrase";

    public const string Teacher = "teacher";
    public const string You = "you";

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TopicId { get; set; }

    /// <summary><see cref="Line"/> or <see cref="Phrase"/>.</summary>
    public string Kind { get; set; } = Line;

    /// <summary>
    /// <see cref="Teacher"/> or <see cref="You"/> on a conversation line; empty on a
    /// phrase, which nobody says.
    /// </summary>
    public string Speaker { get; set; } = "";

    /// <summary>The English side — the sentence, or the phrase itself.</summary>
    public string En { get; set; } = "";

    /// <summary>The Vietnamese side — the translation, or the phrase's meaning.</summary>
    public string Vi { get; set; } = "";

    /// <summary>Order within the lesson. The phrase table simply sits after the lines.</summary>
    public int Position { get; set; }
}
