using System.Text.Json;
using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Common;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.English;

/// <summary>
/// Saved vocabulary and sentences kept from speaking practice.
/// </summary>
public sealed class EnglishService(
    IEnglishWordRepository words,
    ISavedSentenceRepository sentences,
    ICurrentUser user,
    IUnitOfWork uow)
{
    // Rows are mapped here, in memory, on purpose.
    //
    // Reaching for `row.Data.RootElement` inside a LINQ projection looks harmless and is
    // not: EF then wants to read the jsonb column as JsonElement? while the property maps
    // to JsonDocument, and gives up while compiling the query with
    //   "No coercion operator is defined between types 'JsonDocument' and 'JsonElement?'"
    // Nothing caught that, so GET /words answered a bare 500 with an empty body for its
    // whole life. Ordinary object construction, outside any IQueryable, is always safe.
    private static EnglishWordDto ToDto(EnglishWord r) => new(r.Id, r.Word, r.Data.RootElement, r.CreatedAt);

    private static SavedSentenceDto ToDto(SavedSentence r) => new(r.Id, r.Text, r.Note, r.CreatedAt);

    // ---------- Saved vocabulary ----------

    public async Task<Paged<EnglishWordDto>> ListWordsAsync(int? limit, int? offset, CancellationToken ct = default)
    {
        var take = Page.Limit(limit);
        var skip = Page.Offset(offset);

        var total = await words.CountAsync(ct);
        var rows = await words.ListAsync(skip, take, ct);

        return new Paged<EnglishWordDto>(total, take, skip, rows.Select(ToDto).ToList());
    }

    public async Task<EnglishWordDto> AddWordAsync(SaveEnglishWordRequest body, CancellationToken ct = default)
    {
        var word = (body.Word ?? "").Trim();
        if (word.Length == 0) throw new ValidationException("Thiếu từ vựng.");

        var row = new EnglishWord
        {
            UserId = user.Id,
            Word = word,
            // Copied out of the request buffer: the incoming JsonElement is only valid
            // while the request body is alive, and EF writes this well after that.
            Data = JsonDocument.Parse(body.Data.GetRawText()),
        };
        words.Add(row);
        await uow.SaveChangesAsync(ct);

        return ToDto(row);
    }

    /// <returns><c>false</c> when no such word belongs to the current user.</returns>
    public async Task<bool> DeleteWordAsync(Guid id, CancellationToken ct = default)
    {
        var row = await words.FindAsync(id, ct);
        if (row is null) return false;

        words.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    // ---------- Sentences kept from speaking practice ----------

    public async Task<Paged<SavedSentenceDto>> ListSentencesAsync(
        int? limit, int? offset, string? query, CancellationToken ct = default)
    {
        var take = Page.Limit(limit);
        var skip = Page.Offset(offset);

        var (total, rows) = await sentences.SearchAsync(query, skip, take, ct);

        return new Paged<SavedSentenceDto>(total, take, skip, rows.Select(ToDto).ToList());
    }

    /// <summary>
    /// Keeps one sentence.
    /// <para>Saving the same text twice is a no-op rather than an error: the client shows a
    /// bookmark toggle, and a double tap must not create duplicates. The caller can tell
    /// the two apart because the HTTP status differs — 201 for a new row, 200 for one that
    /// already existed.</para>
    /// </summary>
    public async Task<(SavedSentenceDto Dto, bool Created)> AddSentenceAsync(
        SaveSentenceRequest body, CancellationToken ct = default)
    {
        var text = (body.Text ?? "").Trim();
        if (text.Length == 0) throw new ValidationException("Thiếu nội dung câu.");

        // Runs through the query filter, so it only ever finds the caller's own rows: two
        // people saving the same sentence each get one of their own.
        var existing = await sentences.FindByTextAsync(text, ct);
        if (existing is not null) return (ToDto(existing), false);

        var row = new SavedSentence
        {
            UserId = user.Id,
            Text = text,
            Note = (body.Note ?? "").Trim(),
        };
        sentences.Add(row);
        await uow.SaveChangesAsync(ct);

        return (ToDto(row), true);
    }

    /// <returns><c>false</c> when no such sentence belongs to the current user.</returns>
    public async Task<bool> DeleteSentenceAsync(Guid id, CancellationToken ct = default)
    {
        var row = await sentences.FindAsync(id, ct);
        if (row is null) return false;

        sentences.Remove(row);
        await uow.SaveChangesAsync(ct);
        return true;
    }
}
