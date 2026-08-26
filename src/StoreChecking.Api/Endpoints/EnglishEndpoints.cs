using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreChecking.Api.Auth;
using StoreChecking.Api.Data;
using StoreChecking.Api.Dtos;
using StoreChecking.Api.Models;

namespace StoreChecking.Api.Endpoints;

public static class EnglishEndpoints
{
    /// <summary>
    /// Upper bound on how many rows one listing returns. The saved-sentence list is
    /// expected to grow for years of daily practice, so it must not be fetched whole
    /// on every page load.
    /// </summary>
    private const int MaxPageSize = 200;

    private static int Clamp(int? limit) =>
        limit is null or < 1 ? 50 : Math.Min(limit.Value, MaxPageSize);

    // Paging MUST order by something unique. created_at alone is not: rows written in the
    // same transaction share it exactly (now() is transaction time), and ties make the
    // order undefined between queries — page 2 then repeats rows already shown on page 1.
    // Id breaks the tie and gives a total, stable order.

    public static RouteGroupBuilder MapEnglish(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/english").RequireAuthorization().WithTags("Tiếng Anh");

        // ---------- Saved vocabulary ----------

        // GET /api/english/words?limit=50&offset=0
        g.MapGet("/words", async (int? limit, int? offset, AppDbContext db) =>
        {
            var take = Clamp(limit);
            var skip = Math.Max(offset ?? 0, 0);

            var total = await db.EnglishWords.CountAsync();
            var rows = await db.EnglishWords
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Skip(skip).Take(take)
                .Select(x => new EnglishWordDto(x.Id, x.Word, x.Data.RootElement, x.CreatedAt))
                .ToListAsync();

            return Results.Ok(new { total, limit = take, offset = skip, items = rows });
        })
        .WithSummary("Từ vựng đã lưu")
        .WithDescription("Mới nhất trước. limit mặc định 50, tối đa 200.");

        // POST /api/english/words
        g.MapPost("/words", async (SaveEnglishWordRequest body, AppDbContext db, CurrentUser me) =>
        {
            var word = (body.Word ?? "").Trim();
            if (word.Length == 0)
                return Results.BadRequest(new { error = "Thiếu từ vựng." });

            var row = new EnglishWord
            {
                UserId = me.Id,
                Word = word,
                // Copy out of the request buffer: the incoming JsonElement is only valid
                // while the request body is alive, and EF writes this after that point.
                Data = JsonDocument.Parse(body.Data.GetRawText()),
            };
            db.EnglishWords.Add(row);
            await db.SaveChangesAsync();

            return Results.Created($"/api/english/words/{row.Id}",
                new EnglishWordDto(row.Id, row.Word, row.Data.RootElement, row.CreatedAt));
        })
        .WithSummary("Lưu một từ kèm kết quả AI");

        // DELETE /api/english/words/{id}
        g.MapDelete("/words/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var row = await db.EnglishWords.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null) return Results.NotFound();

            db.EnglishWords.Remove(row);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithSummary("Xoá một từ đã lưu");

        // ---------- Sentences kept from speaking practice ----------

        // GET /api/english/sentences?limit=50&offset=0&q=
        g.MapGet("/sentences", async (int? limit, int? offset, string? q, AppDbContext db) =>
        {
            var take = Clamp(limit);
            var skip = Math.Max(offset ?? 0, 0);

            var query = db.SavedSentences.AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                query = query.Where(x => EF.Functions.ILike(x.Text, $"%{needle}%")
                                      || EF.Functions.ILike(x.Note, $"%{needle}%"));
            }

            var total = await query.CountAsync();
            var rows = await query
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Skip(skip).Take(take)
                .Select(x => new SavedSentenceDto(x.Id, x.Text, x.Note, x.CreatedAt))
                .ToListAsync();

            return Results.Ok(new { total, limit = take, offset = skip, items = rows });
        })
        .WithSummary("Câu đã lưu khi luyện nói")
        .WithDescription("Mới nhất trước. `q` tìm trong cả nội dung câu lẫn ghi chú, không phân biệt hoa thường.");

        // POST /api/english/sentences
        g.MapPost("/sentences", async (SaveSentenceRequest body, AppDbContext db, CurrentUser me) =>
        {
            var text = (body.Text ?? "").Trim();
            if (text.Length == 0)
                return Results.BadRequest(new { error = "Thiếu nội dung câu." });

            // Saving the same sentence twice is a no-op rather than an error: the client
            // shows a bookmark toggle, and a double tap should not create duplicates.
            var existing = await db.SavedSentences.FirstOrDefaultAsync(x => x.Text == text);
            if (existing is not null)
                return Results.Ok(new SavedSentenceDto(existing.Id, existing.Text, existing.Note, existing.CreatedAt));

            var row = new SavedSentence
            {
                UserId = me.Id,
                Text = text,
                Note = (body.Note ?? "").Trim(),
            };
            db.SavedSentences.Add(row);
            await db.SaveChangesAsync();

            return Results.Created($"/api/english/sentences/{row.Id}",
                new SavedSentenceDto(row.Id, row.Text, row.Note, row.CreatedAt));
        })
        .WithSummary("Lưu một câu")
        .WithDescription("Lưu trùng câu thì trả lại bản ghi cũ, không tạo thêm dòng.");

        // DELETE /api/english/sentences/{id}
        g.MapDelete("/sentences/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var row = await db.SavedSentences.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null) return Results.NotFound();

            db.SavedSentences.Remove(row);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithSummary("Xoá một câu đã lưu");

        return g;
    }
}
