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
        Json.HasExactly(note, "id", "title", "content", "images", "createdAt", "updatedAt");

        // Ghi chú mới phải có mảng ảnh RỖNG, không phải null: phía giao diện duyệt thẳng
        // mảng này, và null thì mọi ghi chú chưa đính ảnh sẽ làm vỡ danh sách.
        Assert.Equal(JsonValueKind.Array, note.GetProperty("images").ValueKind);
        Assert.Equal(0, note.GetProperty("images").GetArrayLength());
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

    /// <summary>Một ảnh JPEG nhỏ nhất có thể, đủ để máy chủ nhận là ảnh thật.</summary>
    private static byte[] TinyJpeg() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a" +
        "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAA" +
        "AAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==");

    [DbFact]
    public async Task Dinh_nhieu_anh_vao_ghi_chu_roi_go_ra()
    {
        var c = NewUser();
        var id = (await Json.Read(await c.PostJson("/api/notes",
            new { title = "Mẫu tin nhắn", content = "xin chào" }))).GetProperty("id").GetGuid();

        async Task<System.Text.Json.JsonElement> Attach(string name)
        {
            using var form = new MultipartFormDataContent();
            var img = new ByteArrayContent(TinyJpeg());
            img.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            form.Add(img, "file", name);

            var res = await c.PostAsync($"/api/notes/{id}/images", form);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return await Json.Read(res);
        }

        await Attach("mot.jpg");
        var two = await Attach("hai.jpg");

        // Đính nhiều ảnh, và thứ tự giữ nguyên như lúc thêm.
        Assert.Equal(2, two.GetProperty("images").GetArrayLength());

        var first = two.GetProperty("images")[0].GetString()!;

        // Ảnh xem lại được, và đúng là ảnh chứ không phải trang lỗi.
        var file = await c.GetAsync($"/api/notes/images/{first}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("image/jpeg", file.Content.Headers.ContentType?.MediaType);

        // Gỡ một ảnh thì chỉ ảnh đó đi, ảnh còn lại ở nguyên.
        var after = await Json.Read(await c.DeleteAsync($"/api/notes/{id}/images/{first}"));
        Assert.Equal(1, after.GetProperty("images").GetArrayLength());
        Assert.NotEqual(first, after.GetProperty("images")[0].GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/notes/images/{first}")).StatusCode);
    }

    // Ảnh phải đi theo ghi chú. Để lại thì chúng nằm trên đĩa mãi mà không còn chỗ nào
    // hiển thị để mà biết là chúng còn tồn tại.
    [DbFact]
    public async Task Xoa_ghi_chu_thi_anh_di_theo()
    {
        var c = NewUser();
        var id = (await Json.Read(await c.PostJson("/api/notes",
            new { title = (string?)null, content = "có ảnh" }))).GetProperty("id").GetGuid();

        using var form = new MultipartFormDataContent();
        var img = new ByteArrayContent(TinyJpeg());
        img.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(img, "file", "anh.jpg");

        var attached = await Json.Read(await c.PostAsync($"/api/notes/{id}/images", form));
        var name = attached.GetProperty("images")[0].GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/notes/images/{name}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/notes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/notes/images/{name}")).StatusCode);
    }
}
