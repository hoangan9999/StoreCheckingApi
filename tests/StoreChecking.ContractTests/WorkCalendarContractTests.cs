using System.Net;

namespace StoreChecking.ContractTests;

[Collection(nameof(ApiCollection))]
public sealed class WorkCalendarContractTests(ApiFactory api)
{
    // A fresh user per test: the global query filters keep their rows apart, so no
    // truncation between tests and no ordering dependency between them.
    private HttpClient NewUser() => api.ClientFor(Guid.NewGuid());

    // ---------- Day cells ----------

    [DbFact]
    public async Task Days_thieu_from_hoac_to_thi_400_kem_thong_bao()
    {
        var res = await NewUser().GetAsync("/api/work-calendar/days");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await Json.Read(res);
        Assert.Equal("Cần from và to dạng YYYY-MM-DD.", body.GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Days_to_nho_hon_from_thi_400()
    {
        var res = await NewUser().GetAsync("/api/work-calendar/days?from=2026-10-10&to=2026-10-01");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await Json.Read(res);
        Assert.Equal("to phải >= from.", body.GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Ghi_mot_o_ngay_roi_doc_lai_thay_dung_hinh_dang()
    {
        var c = NewUser();

        var put = await c.PutJson("/api/work-calendar/days/2026-10-01", new { note = " Trực ca 2 ", color = "do" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var cell = await Json.Read(put);
        Json.HasExactly(cell, "id", "day", "note", "color");
        Assert.Equal("2026-10-01", cell.GetProperty("day").GetString());
        Assert.Equal("do", cell.GetProperty("color").GetString());
        // The endpoint trims only when deciding whether the cell is empty; what it stores
        // is the raw note. Pinning this down because it is easy to "tidy up" by accident.
        Assert.Equal(" Trực ca 2 ", cell.GetProperty("note").GetString());

        var list = await Json.Read(await c.GetAsync("/api/work-calendar/days?from=2026-10-01&to=2026-10-31"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("2026-10-01", list[0].GetProperty("day").GetString());
    }

    [DbFact]
    public async Task Ghi_lai_cung_mot_ngay_thi_sua_cho_cu_chu_khong_them_dong()
    {
        var c = NewUser();

        var first = await Json.Read(await c.PutJson("/api/work-calendar/days/2026-10-05", new { note = "lần 1", color = (string?)null }));
        var second = await Json.Read(await c.PutJson("/api/work-calendar/days/2026-10-05", new { note = "lần 2", color = "cam" }));

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal("lần 2", second.GetProperty("note").GetString());

        var list = await Json.Read(await c.GetAsync("/api/work-calendar/days?from=2026-10-01&to=2026-10-31"));
        Assert.Equal(1, list.GetArrayLength());
    }

    // This is the destructive behaviour CLAUDE.md flags as needing care. It is deliberate:
    // an empty cell must not leave a row behind. Pinned here so a refactor cannot quietly
    // turn "delete" into "store an empty row" — or worse, the other way round.
    [DbFact]
    public async Task O_ngay_khong_ghi_chu_va_khong_mau_thi_bi_XOA_va_tra_204()
    {
        var c = NewUser();
        await c.PutJson("/api/work-calendar/days/2026-10-09", new { note = "sẽ bị xoá", color = "vang" });

        var cleared = await c.PutJson("/api/work-calendar/days/2026-10-09", new { note = "   ", color = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        var list = await Json.Read(await c.GetAsync("/api/work-calendar/days?from=2026-10-01&to=2026-10-31"));
        Assert.Equal(0, list.GetArrayLength());
    }

    [DbFact]
    public async Task Ngay_sai_dinh_dang_thi_400()
    {
        var res = await NewUser().PutJson("/api/work-calendar/days/01-10-2026", new { note = "x", color = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await Json.Read(res);
        Assert.Equal("Ngày phải dạng YYYY-MM-DD.", body.GetProperty("error").GetString());
    }

    // ---------- Month notes ----------

    [DbFact]
    public async Task Notes_thieu_period_thi_400()
    {
        var res = await NewUser().GetAsync("/api/work-calendar/notes");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await Json.Read(res);
        Assert.Equal("Cần period dạng YYYY-MM-01.", body.GetProperty("error").GetString());
    }

    [DbFact]
    public async Task Vong_doi_mot_dong_ghi_chu_thang()
    {
        var c = NewUser();

        var created = await c.PostJson("/api/work-calendar/notes", new { period = "2026-10-01", sort = 3 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var note = await Json.Read(created);
        Json.HasExactly(note, "id", "period", "content", "sort");
        Assert.Equal("2026-10-01", note.GetProperty("period").GetString());
        Assert.Equal("", note.GetProperty("content").GetString());   // created empty, filled in later
        Assert.Equal(3, note.GetProperty("sort").GetInt32());

        var id = note.GetProperty("id").GetGuid();
        Assert.Equal($"/api/work-calendar/notes/{id}", created.Headers.Location?.ToString());

        var updated = await c.PutJson($"/api/work-calendar/notes/{id}", new { content = "nhớ chốt công" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("nhớ chốt công", (await Json.Read(updated)).GetProperty("content").GetString());

        var listed = await Json.Read(await c.GetAsync("/api/work-calendar/notes?period=2026-10-01"));
        Assert.Equal(1, listed.GetArrayLength());
        Assert.Equal("nhớ chốt công", listed[0].GetProperty("content").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/work-calendar/notes/{id}")).StatusCode);
        Assert.Equal(0, (await Json.Read(await c.GetAsync("/api/work-calendar/notes?period=2026-10-01"))).GetArrayLength());
    }

    [DbFact]
    public async Task Sua_hoac_xoa_dong_khong_ton_tai_thi_404()
    {
        var c = NewUser();
        var missing = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await c.PutJson($"/api/work-calendar/notes/{missing}", new { content = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/work-calendar/notes/{missing}")).StatusCode);
    }

    [DbFact]
    public async Task Ghi_chu_thang_sap_theo_sort()
    {
        var c = NewUser();
        foreach (var sort in new[] { 5, 1, 3 })
            await c.PostJson("/api/work-calendar/notes", new { period = "2026-11-01", sort });

        var listed = await Json.Read(await c.GetAsync("/api/work-calendar/notes?period=2026-11-01"));
        Assert.Equal([1, 3, 5], listed.EnumerateArray().Select(x => x.GetProperty("sort").GetInt32()).ToArray());
    }
}
