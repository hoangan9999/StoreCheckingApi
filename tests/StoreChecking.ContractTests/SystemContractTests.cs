using System.Net;

namespace StoreChecking.ContractTests;

[Collection(nameof(ApiCollection))]
public sealed class SystemContractTests(ApiFactory api)
{
    [DbFact]
    public async Task Health_khong_can_token_va_bao_dung_ba_truong()
    {
        var res = await api.AnonymousClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json.Read(res);

        // tools/deploy.ps1 waits for `version` to match the commit it just pushed. Rename
        // or drop it and every deploy silently times out instead of reporting success.
        Json.HasExactly(body, "ok", "db", "version", "dbMs", "idleSec");
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.True(body.GetProperty("db").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));

        // dbMs và idleSec là để chẩn đoán những lần chậm sau khi app nghỉ lâu: dbMs gần
        // bằng tổng thời gian phản hồi thì nút thắt ở database, còn nhỏ mà vẫn chậm thì
        // nút thắt nằm chỗ khác. Vô nghĩa nếu thiếu, nên khoá lại ở đây.
        Assert.True(body.GetProperty("dbMs").GetInt64() >= 0);
        Assert.True(body.GetProperty("idleSec").GetInt32() >= 0);
    }

    [DbFact]
    public async Task Me_khong_co_token_thi_401()
    {
        var res = await api.AnonymousClient().GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [DbFact]
    public async Task Me_tra_dung_user_id_lay_tu_claim_sub()
    {
        var me = Guid.NewGuid();
        var res = await api.ClientFor(me, "ai-do@example.com").GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json.Read(res);

        Json.HasExactly(body, "userId", "email");
        Assert.Equal(me, body.GetProperty("userId").GetGuid());
        Assert.Equal("ai-do@example.com", body.GetProperty("email").GetString());
    }

    [DbFact]
    public async Task Moi_route_du_lieu_deu_doi_token()
    {
        var anon = api.AnonymousClient();

        foreach (var url in new[]
                 {
                     "/api/work-calendar/days?from=2026-10-01&to=2026-10-02",
                     "/api/work-calendar/notes?period=2026-10-01",
                     "/api/english/words",
                     "/api/english/sentences",
                 })
        {
            var res = await anon.GetAsync(url);
            Assert.True(res.StatusCode == HttpStatusCode.Unauthorized,
                $"{url} phải trả 401 khi không có token, nhưng trả {(int)res.StatusCode}.");
        }
    }
}
