using System.Text.Json;

namespace StoreChecking.Application.English;

/// <summary>A saved vocabulary word as returned to the client.</summary>
public record EnglishWordDto(Guid Id, string Word, JsonElement Data, DateTimeOffset CreatedAt);

/// <summary>Save a word together with the AI result generated for it.</summary>
public record SaveEnglishWordRequest(string Word, JsonElement Data);

/// <summary>A sentence kept from speaking practice, as returned to the client.</summary>
public record SavedSentenceDto(
    Guid Id, string Text, string Note, string Context, DateTimeOffset CreatedAt);

/// <summary>
/// Keep one sentence from a speaking session.
/// <para><c>Context</c> is the line before it in the conversation — the question an answer
/// answers. Reviewing a saved answer without it is guesswork.</para>
/// </summary>
public record SaveSentenceRequest(string Text, string? Note, string? Context);

// ---------- Bài học theo chủ đề ----------

/// <summary>One row of a lesson, as sent and returned.</summary>
/// <param name="Kind"><c>line</c> for a conversation line, <c>phrase</c> for the phrase table.</param>
/// <param name="Speaker"><c>teacher</c> or <c>you</c>; empty on a phrase.</param>
public record LessonLineDto(Guid Id, string Kind, string Speaker, string En, string Vi);

/// <summary>One row being written. No id: the whole lesson is replaced in one go.</summary>
public record SaveLessonLine(string Kind, string? Speaker, string En, string? Vi);

/// <summary>A topic in the list — without its lines, which the list never shows.</summary>
/// <param name="CategoryId">
/// Which category it is filed under, or <c>null</c> for "chưa phân loại".
/// <para>The id, not the name: the client already holds the category list, and sending
/// the text would be a second copy of it that goes stale the moment one is renamed.</para>
/// </param>
public record TopicSummaryDto(
    Guid Id, string Title, string Kind, int LineCount, int PhraseCount, bool Studying,
    Guid? CategoryId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>A topic opened: its title and every row of it, in order.</summary>
public record TopicDto(
    Guid Id, string Title, string Kind, IReadOnlyList<LessonLineDto> Lines,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>One topic being created or replaced.</summary>
public record SaveTopicRequest(string Title, IReadOnlyList<SaveLessonLine>? Lines);

/// <summary>
/// Import whole lessons at once.
/// <para>A list because one paste normally carries several topics — the source is a whole
/// conversation, not a single lesson. One call so a paste either lands complete or does
/// not land at all.</para>
/// </summary>
/// <param name="Kind">
/// Which list these belong to — <c>lesson</c> (default) or <c>translate</c>. One per
/// request, not one per topic: a single paste, or a single round of AI generation, is
/// always all of one kind.
/// </param>
public record ImportTopicsRequest(IReadOnlyList<SaveTopicRequest>? Topics, string? Kind = null);

/// <summary>
/// Tick or untick "đang học dang dở" on one topic.
/// <para>Its own tiny endpoint rather than a field on <see cref="SaveTopicRequest"/>:
/// ticking a box must not need the lesson's whole contents in hand, and saving an edited
/// lesson must not be able to clear the mark by leaving the field out.</para>
/// </summary>
public record SetStudyingRequest(bool Studying);

/// <summary>One label topics can be filed under.</summary>
public record CategoryDto(Guid Id, string Name, DateTimeOffset CreatedAt);

/// <summary>A category being created, or renamed.</summary>
public record SaveCategoryRequest(string? Name);

/// <summary>
/// File one topic under a category, or take it out of every category.
/// <para><c>null</c> means "chưa phân loại" and is a normal value, not a missing one —
/// which is why this is its own endpoint rather than a field on
/// <see cref="SaveTopicRequest"/>: there, leaving it out and clearing it would look
/// identical, and every save of an edited lesson would quietly unfile it.</para>
/// </summary>
public record SetTopicCategoryRequest(Guid? CategoryId);
