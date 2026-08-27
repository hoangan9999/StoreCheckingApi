using System.Net;
using System.Text.Json;

namespace StoreChecking.ContractTests;

[Collection(nameof(ApiCollection))]
public sealed class NotesContractTests(ApiFactory api)
{
    private HttpClient NewUser() => api.ClientFor(Guid.NewGuid());

    [DbFact]
    public async Task Chua_co_ghi_chu_thi_tra_mang_rong()
    {
        var res = await NewUser().GetAsync("/api/notes");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(0, (await Json.Read(res)).GetArrayLength());
    }

    [DbFact]
    public async Task Them_ghi_chu_roi_doc_lai()
    {
        var c = NewUser();

        var created = await c.PostJson("/api/notes", new { title = "  STK Vietcombank  ", content = "0123456789" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var note = await Json.Read(created);
        Json.HasExactly(note, "id", "title", "content", "createdAt", "updatedAt");
        Assert.Equal("STK Vietcombank", note.GetProperty("title").GetString());   // trimmed
        Assert.Equal("0123456789", note.GetProperty("content").GetString());

        var id = note.GetProperty("id").GetGuid();
        Assert.Equal($"/api/notes/{id}", created.Headers.Location?.ToString());

        var listed = await Json.Read(await c.GetAsync("/api/notes"));
        Assert.Equal(1, listed.GetArrayLength());
        Assert.Equal(id, listed[0].GetProperty("id").GetGuid());
    }

    // Notes exist to be copied to the clipboard verbatim. A message template's own leading
    // blank line or indentation is content, not stray whitespace, so nothing trims it.
    [DbFact]
    public async Task Noi_dung_KHONG_bi_cat_khoang_trang()
    {
        var c = NewUser();
        const string template = "  Chào anh/chị,\n\n  Đơn của mình là:  ";

        var note = await Json.Read(await c.PostJson("/api/notes", new { title = (string?)null, content = template }));
        Assert.Equal(template, note.GetProperty("content").GetString());

        var listed = await Json.Read(await c.GetAsync("/api/notes"));
        Assert.Equal(template, listed[0].GetProperty("content").GetString());
    }

    // The client renders the heading with a truthiness check, so a title of spaces would
    // draw an empty heading box. Blank becomes null instead.
    [DbFact]
    public async Task Tieu_de_toan_khoang_trang_thi_thanh_null()
    {
        var c = NewUser();
        var note = await Json.Read(await c.PostJson("/api/notes", new { title = "   ", content = "co noi dung" }));

        Assert.Equal(JsonValueKind.Null, note.GetProperty("title").ValueKind);
    }

    [DbFact]
    public async Task Ghi_chu_rong_van_luu_duoc()
    {
        // Supabase allowed this (content defaults to an empty string), so the new API must
        // too — otherwise notes that already exist could not be saved again.
        var res = await NewUser().PostJson("/api/notes", new { title = (string?)null, content = (string?)null });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal("", (await Json.Read(res)).GetProperty("content").GetString());
    }

    [DbFact]
    public async Task Sua_ghi_chu()
    {
        var c = NewUser();
        var id = (await Json.Read(await c.PostJson("/api/notes", new { title = "cu", content = "noi dung cu" })))
            .GetProperty("id").GetGuid();

        var updated = await c.PutJson($"/api/notes/{id}", new { title = "moi", content = "noi dung moi" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var note = await Json.Read(updated);
        Assert.Equal(id, note.GetProperty("id").GetGuid());
        Assert.Equal("moi", note.GetProperty("title").GetString());
        Assert.Equal("noi dung moi", note.GetProperty("content").GetString());

        var listed = await Json.Read(await c.GetAsync("/api/notes"));
        Assert.Equal(1, listed.GetArrayLength());     // edited, not duplicated
    }

    [DbFact]
    public async Task Sua_xong_thi_nhay_len_dau_danh_sach()
    {
        var c = NewUser();
        var first = (await Json.Read(await c.PostJson("/api/notes", new { title = "A", content = "a" }))).GetProperty("id").GetGuid();
        var second = (await Json.Read(await c.PostJson("/api/notes", new { title = "B", content = "b" }))).GetProperty("id").GetGuid();

        // Both were created in the same second, so this also proves the Id tie-break keeps
        // the order stable rather than leaving it up to the database.
        await c.PutJson($"/api/notes/{first}", new { title = "A", content = "a sua roi" });

        var listed = await Json.Read(await c.GetAsync("/api/notes"));
        Assert.Equal(2, listed.GetArrayLength());
        Assert.Equal(first, listed[0].GetProperty("id").GetGuid());
        Assert.Equal(second, listed[1].GetProperty("id").GetGuid());
    }

    [DbFact]
    public async Task Xoa_ghi_chu()
    {
        var c = NewUser();
        var id = (await Json.Read(await c.PostJson("/api/notes", new { title = "xoa", content = "x" })))
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/notes/{id}")).StatusCode);
        Assert.Equal(0, (await Json.Read(await c.GetAsync("/api/notes"))).GetArrayLength());
    }

    [DbFact]
    public async Task Sua_hoac_xoa_ghi_chu_khong_ton_tai_thi_404()
    {
        var c = NewUser();
        var missing = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await c.PutJson($"/api/notes/{missing}", new { title = "x", content = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/notes/{missing}")).StatusCode);
    }

    [DbFact]
    public async Task Khong_co_token_thi_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.AnonymousClient().GetAsync("/api/notes")).StatusCode);
    }
}
