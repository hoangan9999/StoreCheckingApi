using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>
/// A label the user groups topics under: "Công việc", "Du lịch", "Giao tiếp hàng ngày".
/// <para><see cref="Id"/> is the identity; <see cref="Name"/> is only what gets shown and
/// may be renamed freely. Same reasoning as <see cref="ProductKind"/>: a category held as
/// text on each topic means one typo silently makes a second category, and one rename has
/// to be applied to every topic that carries the old spelling.</para>
/// <para>One list shared by both <see cref="EnglishTopic.Lesson"/> and
/// <see cref="EnglishTopic.Translate"/>. A category describes subject matter, not the
/// exercise format, so splitting it per list would mean creating "Du lịch" twice and
/// letting the two drift apart.</para>
/// </summary>
public class EnglishCategory : IOwnedByUser
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Display text. NOT the identity — never look a category up by this.</summary>
    public string Name { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}

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

    /// <summary>A conversation to study.</summary>
    public const string Lesson = "lesson";

    /// <summary>A Vietnamese passage to translate, with its suggested wording.</summary>
    public const string Translate = "translate";

    public string Title { get; set; } = "";

    /// <summary>
    /// <see cref="Lesson"/> or <see cref="Translate"/> — which of the two lists this topic
    /// belongs to.
    /// <para>⚠️ This is what keeps the two lists apart. The two are drawn on screen in
    /// completely different ways, so a topic in the wrong list renders wrong, not merely
    /// ugly.</para>
    /// </summary>
    public string Kind { get; set; } = Lesson;

    /// <summary>
    /// Where it sits in the user's own ordering. A new topic takes <c>min(position) - 1</c>
    /// so it lands at the top, the same way notes do.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Marked by the user as a lesson they are part-way through.
    /// <para>It exists so that closing the app and coming back does not lose which lesson
    /// was being worked on — the titles alone say nothing about progress. A flag per topic
    /// rather than one "current lesson" field, because having two on the go at once is
    /// normal and a single field would silently clear the first one.</para>
    /// </summary>
    public bool Studying { get; set; }

    /// <summary>
    /// Which <see cref="EnglishCategory"/> this topic is filed under; <c>null</c> means
    /// not filed yet.
    /// <para><c>null</c> is a normal state, not a gap to be filled in: every topic that
    /// existed before categories did starts there, and a freshly pasted lesson has not
    /// been looked at yet. The list shows those under "Chưa phân loại".</para>
    /// <para>Deleting a category sets this back to <c>null</c> (<c>on delete set null</c>
    /// in db/025) — removing a label must never remove the lessons wearing it.</para>
    /// </summary>
    public Guid? CategoryId { get; set; }

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

    /// <summary>A row of the "Cụm từ hay" table, or of a translation's suggested wording.</summary>
    public const string Phrase = "phrase";

    /// <summary>
    /// A passage: the Vietnamese to translate in <see cref="Vi"/>, its English in
    /// <see cref="En"/>. Only ever found in a <see cref="EnglishTopic.Translate"/> topic.
    /// </summary>
    public const string Para = "para";

    /// <summary>
    /// The level tag heading a group of interview questions: "Junior", "Middle",
    /// "Senior", "Junior → Middle". <see cref="En"/> holds the tag, <see cref="Vi"/>
    /// the note printed beside it ("câu hỏi đào sâu") when there is one.
    /// </summary>
    public const string Level = "level";

    /// <summary>
    /// A "Giải thích" block: Vietnamese commentary on the answer above it, kept
    /// verbatim in <see cref="Vi"/> with its line breaks and any code sample.
    /// <para>⚠️ The one kind whose <see cref="En"/> is empty. Putting the text in
    /// <see cref="En"/> instead would put Vietnamese in the English column, where "Ẩn
    /// tiếng Việt" cannot hide it and the en-US voice would read it aloud.</para>
    /// </summary>
    public const string Note = "note";

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
