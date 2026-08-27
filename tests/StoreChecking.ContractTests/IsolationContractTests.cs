using System.Net;

namespace StoreChecking.ContractTests;

/// <summary>
/// The most important tests in this suite.
/// <para>On Supabase the database itself refused to hand back another user's rows. That
/// safety net is gone here: all that stops a leak is EF Core's global query filter, which
/// is a convention someone has to remember on every new table. These tests are what turns
/// that convention into something enforced.</para>
/// <para>Every case checks BOTH directions — that the other user cannot list the row, and
/// that they cannot reach it by Id even when they know it.</para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class IsolationContractTests(ApiFactory api)
{
    [DbFact]
    public async Task Tu_vung_cua_nguoi_khac_khong_doc_duoc_va_khong_xoa_duoc()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var mine = await Json.Read(await a.PostJson("/api/english/words",
            new { word = "private", data = new { meaning = "cua rieng A" } }));
        var id = mine.GetProperty("id").GetGuid();

        var seenByB = await Json.Read(await b.GetAsync("/api/english/words"));
        Assert.Equal(0, seenByB.GetProperty("total").GetInt32());
        Assert.Equal(0, seenByB.GetProperty("items").GetArrayLength());

        // Knowing the Id must not help.
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/english/words/{id}")).StatusCode);

        // ...and the row is still there for its owner afterwards.
        Assert.Equal(1, (await Json.Read(await a.GetAsync("/api/english/words"))).GetProperty("total").GetInt32());
    }

    [DbFact]
    public async Task Cau_da_luu_cua_nguoi_khac_khong_doc_duoc_va_khong_xoa_duoc()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var mine = await Json.Read(await a.PostJson("/api/english/sentences",
            new { text = "Only A may see this.", note = (string?)null }));
        var id = mine.GetProperty("id").GetGuid();

        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/english/sentences"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/english/sentences?q=Only%20A"))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/english/sentences/{id}")).StatusCode);
    }

    [DbFact]
    public async Task O_ngay_cua_nguoi_khac_khong_doc_duoc_va_ghi_de_khong_dung_vao_dong_cua_ho()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var mine = await Json.Read(await a.PutJson("/api/work-calendar/days/2026-12-24", new { note = "cua A", color = "do" }));

        var seenByB = await Json.Read(await b.GetAsync("/api/work-calendar/days?from=2026-12-01&to=2026-12-31"));
        Assert.Equal(0, seenByB.GetArrayLength());

        // B writing the SAME date must create a separate row, not overwrite A's. The unique
        // index is on (user_id, day), so both rows can coexist.
        var theirs = await Json.Read(await b.PutJson("/api/work-calendar/days/2026-12-24", new { note = "cua B", color = "xanh" }));
        Assert.NotEqual(mine.GetProperty("id").GetGuid(), theirs.GetProperty("id").GetGuid());

        var aAgain = await Json.Read(await a.GetAsync("/api/work-calendar/days?from=2026-12-01&to=2026-12-31"));
        Assert.Equal(1, aAgain.GetArrayLength());
        Assert.Equal("cua A", aAgain[0].GetProperty("note").GetString());
    }

    [DbFact]
    public async Task Ghi_chu_thang_cua_nguoi_khac_khong_doc_sua_hay_xoa_duoc()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());

        var mine = await Json.Read(await a.PostJson("/api/work-calendar/notes", new { period = "2026-12-01", sort = 1 }));
        var id = mine.GetProperty("id").GetGuid();
        await a.PutJson($"/api/work-calendar/notes/{id}", new { content = "bi mat cua A" });

        Assert.Equal(0, (await Json.Read(await b.GetAsync("/api/work-calendar/notes?period=2026-12-01"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutJson($"/api/work-calendar/notes/{id}", new { content = "B sua trom" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/work-calendar/notes/{id}")).StatusCode);

        // Untouched.
        var still = await Json.Read(await a.GetAsync("/api/work-calendar/notes?period=2026-12-01"));
        Assert.Equal("bi mat cua A", still[0].GetProperty("content").GetString());
    }

    [DbFact]
    public async Task Luu_trung_cau_chi_tinh_trong_pham_vi_mot_nguoi()
    {
        var a = api.ClientFor(Guid.NewGuid());
        var b = api.ClientFor(Guid.NewGuid());
        const string text = "Two people can save the same sentence.";

        var first = await a.PostJson("/api/english/sentences", new { text, note = (string?)null });
        var second = await b.PostJson("/api/english/sentences", new { text, note = (string?)null });

        // The de-duplication lookup runs through the query filter, so B gets a row of
        // their own rather than being handed A's.
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(
            (await Json.Read(first)).GetProperty("id").GetGuid(),
            (await Json.Read(second)).GetProperty("id").GetGuid());
    }
}
