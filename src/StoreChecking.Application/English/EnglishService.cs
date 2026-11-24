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

    /// <summary>
    /// Every topic of one kind, in the user's own order, with how many rows each one holds.
    /// </summary>
    /// <param name="kind">
    /// <c>lesson</c> (the default when not given) or <c>translate</c>.
    /// <para>⚠️ Defaulting to <c>lesson</c> rather than "everything" is deliberate: the two
    /// kinds are drawn on screen in completely different ways, so a mixed list renders
    /// wrong rather than merely long — and an older client that asks without the parameter
    /// keeps getting exactly what it got before.</para>
    /// </param>
    public async Task<IReadOnlyList<TopicSummaryDto>> ListTopicsAsync(
        string? kind = null, CancellationToken ct = default)
    {
        var want = TopicKind(kind);
        var rows = await topics.ListAsync(ct);
        var counts = await topics.CountsAsync(ct);

        return rows.Where(t => t.Kind == want).Select(t =>
        {
            var c = counts.TryGetValue(t.Id, out var n) ? n : (Lines: 0, Phrases: 0);
            return new TopicSummaryDto(
                t.Id, t.Title, t.Kind, c.Lines, c.Phrases, t.Studying, t.CreatedAt, t.UpdatedAt);
        }).ToList();
    }

    /// <returns><c>null</c> when the current user has no topic with that id.</returns>
    public async Task<TopicDto?> GetTopicAsync(Guid id, CancellationToken ct = default)
    {
        var t = await topics.FindAsync(id, ct);
        if (t is null) return null;

        var lines = await topics.LinesAsync(t.Id, ct);
        return new TopicDto(
            t.Id, t.Title, t.Kind, lines.Select(ToDto).ToList(), t.CreatedAt, t.UpdatedAt);
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

        var kind = TopicKind(body.Kind);
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
                Kind = kind,
                Position = position++,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var lines = BuildLines(topic.Id, w.Lines);

            topics.Add(topic);
            topics.AddLines(lines);

            made.Add(new TopicSummaryDto(
                topic.Id, topic.Title, topic.Kind,
                lines.Count(l => l.Kind == EnglishLessonLine.Line),
                lines.Count(l => l.Kind == EnglishLessonLine.Phrase),
                topic.Studying, topic.CreatedAt, topic.UpdatedAt));
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
    /// <remarks>
    /// ⚠️ Deliberately does NOT touch <see cref="EnglishTopic.Kind"/> either. A topic does
    /// not change which list it lives in because somebody edited its text, and letting an
    /// edit move it would hand the other list a topic it cannot draw.
    /// </remarks>
    /// <remarks>
    /// ⚠️ Deliberately does NOT touch <see cref="EnglishTopic.Studying"/>. Fixing a typo in
    /// a lesson is not a statement about whether it is still being studied, and quietly
    /// clearing the mark on every save would make the mark useless exactly for the lesson
    /// being worked on. A contract test holds this.
    /// </remarks>
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
        return new TopicDto(topic.Id, topic.Title, topic.Kind, lines.Select(ToDto).ToList(),
            topic.CreatedAt, topic.UpdatedAt);
    }

    /// <summary>
    /// Tick or untick "đang học dang dở" on one topic.
    /// <para>Kept apart from <see cref="SaveTopicAsync"/> so that ticking a box costs one
    /// small call and cannot rewrite any lesson content by accident.</para>
    /// <para><c>UpdatedAt</c> is left alone on purpose: the column means "the lesson was
    /// edited", it is what the list shows as the date, and ticking a box edits nothing.</para>
    /// </summary>
    /// <returns><c>null</c> when the current user has no topic with that id.</returns>
    public async Task<TopicSummaryDto?> SetStudyingAsync(
        Guid id, SetStudyingRequest body, CancellationToken ct = default)
    {
        var topic = await topics.FindAsync(id, ct);
        if (topic is null) return null;

        topic.Studying = body.Studying;
        await uow.SaveChangesAsync(ct);

        var counts = await topics.CountsAsync(ct);
        var c = counts.TryGetValue(topic.Id, out var n) ? n : (Lines: 0, Phrases: 0);
        return new TopicSummaryDto(
            topic.Id, topic.Title, topic.Kind, c.Lines, c.Phrases, topic.Studying,
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
            EnglishLessonLine.Para => EnglishLessonLine.Para,
            _ => throw new ValidationException("Loại dòng phải là 'line', 'phrase' hoặc 'para'."),
        };

    /// <summary>Which list a topic belongs to; anything unknown is refused, not guessed.</summary>
    private static string TopicKind(string? raw)
    {
        var k = (raw ?? "").Trim().ToLowerInvariant();
        if (k.Length == 0) return EnglishTopic.Lesson;
        return k switch
        {
            EnglishTopic.Lesson => EnglishTopic.Lesson,
            EnglishTopic.Translate => EnglishTopic.Translate,
            _ => throw new ValidationException("Mục phải là 'lesson' hoặc 'translate'."),
        };
    }

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

            // Cả bài dịch đứng trên đúng dòng này: `vi` là đề, `en` là bản dịch. Thiếu
            // `vi` thì bài còn mỗi bản tiếng Anh — không còn gì để dịch, mà nhìn vẫn như
            // một bài hợp lệ.
            if (kind == EnglishLessonLine.Para && (l.Vi ?? "").Trim().Length == 0)
                throw new ValidationException("Đoạn văn phải có phần tiếng Việt để dịch.");

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
