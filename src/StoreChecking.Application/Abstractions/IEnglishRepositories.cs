using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Abstractions;

/// <summary>
/// Saved vocabulary.
/// <para>Listing returns whole entities rather than a projection. That is deliberate: the
/// <c>jsonb</c> column maps to <see cref="System.Text.Json.JsonDocument"/>, and touching
/// its <c>RootElement</c> inside a LINQ projection makes EF fail while building the query
/// — "No coercion operator is defined between types 'JsonDocument' and 'JsonElement?'".
/// That bug answered a bare 500 for the entire life of GET /words. Materialising the rows
/// first and mapping in memory removes the whole class of mistake.</para>
/// </summary>
public interface IEnglishWordRepository
{
    Task<int> CountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EnglishWord>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<EnglishWord?> FindAsync(Guid id, CancellationToken ct = default);
    void Add(EnglishWord row);
    void Remove(EnglishWord row);
}

/// <summary>Sentences kept from speaking practice.</summary>
public interface ISavedSentenceRepository
{
    /// <summary>
    /// One round trip for the count and one for the page, both under the same search.
    /// Returned together because the client needs the total to render paging.
    /// </summary>
    Task<(int Total, IReadOnlyList<SavedSentence> Items)> SearchAsync(
        string? query, int skip, int take, CancellationToken ct = default);

    Task<SavedSentence?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Exact-text lookup used to make saving the same sentence twice a no-op.</summary>
    Task<SavedSentence?> FindByTextAsync(string text, CancellationToken ct = default);

    void Add(SavedSentence row);
    void Remove(SavedSentence row);
}

/// <summary>
/// Lessons kept by topic.
/// <para>A topic and its lines always move together — read together, replaced together,
/// deleted together — so one repository owns both rather than two that must be kept in
/// step by whoever remembers to.</para>
/// </summary>
public interface IEnglishTopicRepository
{
    /// <summary>Topics in the user's own order, without their lines.</summary>
    Task<IReadOnlyList<EnglishTopic>> ListAsync(CancellationToken ct = default);

    Task<EnglishTopic?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The smallest position in use, so a new topic can sit above it.
    /// <para><c>null</c> when there are no topics yet.</para>
    /// </summary>
    Task<int?> MinPositionAsync(CancellationToken ct = default);

    /// <summary>Lines of one topic, in order.</summary>
    Task<IReadOnlyList<EnglishLessonLine>> LinesAsync(Guid topicId, CancellationToken ct = default);

    /// <summary>
    /// How many lines each topic has, split by kind — what the list screen shows without
    /// having to load every line of every lesson.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, (int Lines, int Phrases)>> CountsAsync(CancellationToken ct = default);

    void Add(EnglishTopic row);
    void Remove(EnglishTopic row);
    void AddLines(IEnumerable<EnglishLessonLine> rows);
    void RemoveLines(IEnumerable<EnglishLessonLine> rows);
}
