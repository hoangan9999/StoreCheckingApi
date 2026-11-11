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
    IEnglishTopicRepository topics,
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

    private static SavedSentenceDto ToDto(SavedSentence r) =>
        new(r.Id, r.Text, r.Note, r.Context, r.CreatedAt);

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
            Context = (body.Context ?? "").Trim(),
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

    // ---------- Bài học theo chủ đề ----------

    private static LessonLineDto ToDto(EnglishLessonLine r) =>
        new(r.Id, r.Kind, r.Speaker, r.En, r.Vi);

    /// <summary>Every topic, in the user's own order, with how many rows each one holds.</summary>
    public async Task<IReadOnlyList<TopicSummaryDto>> ListTopicsAsync(CancellationToken ct = default)
    {
        var rows = await topics.ListAsync(ct);
        var counts = await topics.CountsAsync(ct);

        return rows.Select(t =>
        {
            var c = counts.TryGetValue(t.Id, out var n) ? n : (Lines: 0, Phrases: 0);
            return new TopicSummaryDto(t.Id, t.Title, c.Lines, c.Phrases, t.CreatedAt, t.UpdatedAt);
        }).ToList();
    }

    /// <returns><c>null</c> when the current user has no topic with that id.</returns>
    public async Task<TopicDto?> GetTopicAsync(Guid id, CancellationToken ct = default)
    {
        var t = await topics.FindAsync(id, ct);
        if (t is null) return null;

        var lines = await topics.LinesAsync(t.Id, ct);
        return new TopicDto(t.Id, t.Title, lines.Select(ToDto).ToList(), t.CreatedAt, t.UpdatedAt);
    }

    /// <summary>
    /// Save whole lessons pasted in from somewhere else.
    /// <para>Takes a list and writes them in one transaction, because one paste normally
    /// carries several topics. Half a paste landing would leave the user to work out which
    /// half, from content they would have to re-read to recognise.</para>
    /// <para>New topics go above the existing ones, newest first, which is where somebody
    /// who just pasted something expects to find it.</para>
    /// </summary>
    public async Task<IReadOnlyList<TopicSummaryDto>> ImportTopicsAsync(
        ImportTopicsRequest body, CancellationToken ct = default)
    {
        var wanted = body.Topics ?? [];
        if (wanted.Count == 0) throw new ValidationException("Không có chủ đề nào để lưu.");

        var position = (await topics.MinPositionAsync(ct) ?? 0) - wanted.Count;
        var now = DateTimeOffset.UtcNow;
        var made = new List<TopicSummaryDto>();

        foreach (var w in wanted)
        {
            var topic = new EnglishTopic
            {
                // Sinh id ở đây chứ không để database sinh. Dòng con mang topic_id, mà
                // nếu id chỉ có SAU khi lưu thì lúc dựng chúng nó mang Guid rỗng và khoá
                // ngoại gãy — hỏng ngay đường thành công, không hỏng đường kiểm tra nào.
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Title = Title(w.Title),
                Position = position++,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var lines = BuildLines(topic.Id, w.Lines);

            topics.Add(topic);
            topics.AddLines(lines);

            made.Add(new TopicSummaryDto(
                topic.Id, topic.Title,
                lines.Count(l => l.Kind == EnglishLessonLine.Line),
                lines.Count(l => l.Kind == EnglishLessonLine.Phrase),
                topic.CreatedAt, topic.UpdatedAt));
        }

        await uow.SaveChangesAsync(ct);
        return made;
    }

    /// <summary>
    /// Replace a topic whole: its title and every row of it.
    /// <para>One call rather than one per row. Editing a typo, deleting a line, adding one
    /// and reordering are all the same operation on the same small list, and splitting
    /// them into four endpoints would be four ways for a lesson to end up half-saved.</para>
    /// </summary>
    /// <returns><c>null</c> when the current user has no topic with that id.</returns>
    public async Task<TopicDto?> SaveTopicAsync(
        Guid id, SaveTopicRequest body, CancellationToken ct = default)
    {
        var topic = await topics.FindAsync(id, ct);
        if (topic is null) return null;

        var lines = BuildLines(topic.Id, body.Lines);

        topic.Title = Title(body.Title);
        topic.UpdatedAt = DateTimeOffset.UtcNow;

        // Thrown away and written again rather than matched up row by row. The rows carry
        // nothing worth keeping — no history, no references — so pairing them by id would
        // be bookkeeping that buys nothing and can go wrong.
        topics.RemoveLines(await topics.LinesAsync(topic.Id, ct));
        topics.AddLines(lines);

        await uow.SaveChangesAsync(ct);
        return new TopicDto(topic.Id, topic.Title, lines.Select(ToDto).ToList(),
            topic.CreatedAt, topic.UpdatedAt);
    }

    /// <returns><c>false</c> when no such topic belongs to the current user.</returns>
    public async Task<bool> DeleteTopicAsync(Guid id, CancellationToken ct = default)
    {
        var topic = await topics.FindAsync(id, ct);
        if (topic is null) return false;

        // The lines go with it by `on delete cascade` in db/022.
        topics.Remove(topic);
        await uow.SaveChangesAsync(ct);
        return true;
    }

    private static string Title(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0) throw new ValidationException("Chủ đề phải có tên.");
        return t;
    }

    private static string Kind(string? raw) =>
        (raw ?? "").Trim().ToLowerInvariant() switch
        {
            EnglishLessonLine.Line => EnglishLessonLine.Line,
            EnglishLessonLine.Phrase => EnglishLessonLine.Phrase,
            _ => throw new ValidationException("Loại dòng phải là 'line' hoặc 'phrase'."),
        };

    /// <summary>
    /// Turn what the client sent into rows, checking the shape as it goes.
    /// <para>Checked here rather than left to the database: the constraint in db/022 would
    /// catch a phrase carrying a speaker, but as a raw Postgres error with no idea which
    /// of fifty rows caused it.</para>
    /// </summary>
    private List<EnglishLessonLine> BuildLines(Guid topicId, IReadOnlyList<SaveLessonLine>? raw)
    {
        var input = raw ?? [];
        if (input.Count == 0) throw new ValidationException("Chủ đề phải có ít nhất một dòng.");

        var rows = new List<EnglishLessonLine>(input.Count);
        var position = 0;

        foreach (var l in input)
        {
            var kind = Kind(l.Kind);
            var speaker = (l.Speaker ?? "").Trim().ToLowerInvariant();
            var en = (l.En ?? "").Trim();

            if (en.Length == 0) throw new ValidationException("Dòng nào cũng phải có phần tiếng Anh.");

            if (kind == EnglishLessonLine.Line)
            {
                if (speaker != EnglishLessonLine.Teacher && speaker != EnglishLessonLine.You)
                    throw new ValidationException("Câu hội thoại phải là của 'teacher' hoặc 'you'.");
            }
            else
            {
                speaker = "";
            }

            rows.Add(new EnglishLessonLine
            {
                UserId = user.Id,
                TopicId = topicId,
                Kind = kind,
                Speaker = speaker,
                En = en,
                Vi = (l.Vi ?? "").Trim(),
                Position = position++,
            });
        }

        return rows;
    }
}
