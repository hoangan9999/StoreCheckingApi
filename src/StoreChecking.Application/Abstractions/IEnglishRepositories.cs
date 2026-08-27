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
