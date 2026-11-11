using System.Net.Http.Json;
using System.Text.Json;

namespace StoreChecking.ContractTests;

/// <summary>
/// Reads responses as raw JSON rather than deserialising into the app's own DTOs.
/// <para>That is the whole point of a contract test: binding to the DTO would happily
/// follow a renamed property and report success while the Angular app broke. Asserting
/// on property names as they appear on the wire is what pins the contract down.</para>
/// </summary>
public static class Json
{
    public static async Task<JsonElement> Read(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>
    /// Đọc body và KHẲNG ĐỊNH là 2xx, nếu không thì ném kèm nguyên văn máy chủ trả về.
    /// <para>Có vì `Json.Read` trên một lỗi 400 trả về object lỗi, rồi dòng `body[0]`
    /// sau đó mới ngã với "requires an element of type 'Array'" — một câu không nói gì về
    /// nguyên nhân. Log CI chỉ đọc được khi đã đăng nhập GitHub, nên câu báo lỗi của test
    /// là chỗ duy nhất lý do thật đi ra được.</para>
    /// </summary>
    public static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"Máy chủ trả {(int)res.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Asserts the object has exactly these property names, no more and no fewer.</summary>
    public static void HasExactly(JsonElement obj, params string[] names)
    {
        var actual = obj.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), actual);
    }

    /// <summary>
    /// The `items` of a paged response, after checking the envelope is intact.
    /// <para>Paged endpoints answer { total, totalAmount, limit, offset, items }, where the
    /// two figures describe the WHOLE filtered set rather than the page. That is what lets
    /// a screen show a running total while holding only what has been scrolled to, so the
    /// envelope is part of the contract and is asserted here rather than assumed.</para>
    /// </summary>
    public static async Task<JsonElement> Items(HttpResponseMessage res)
    {
        var body = await Read(res);
        HasExactly(body, "total", "totalAmount", "limit", "offset", "items");
        return body.GetProperty("items");
    }

    public static StringContent Body(object value) =>
        new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");

    public static Task<HttpResponseMessage> PutJson(this HttpClient c, string url, object body) =>
        c.PutAsync(url, Body(body));

    public static Task<HttpResponseMessage> PostJson(this HttpClient c, string url, object body) =>
        c.PostAsync(url, Body(body));
}
