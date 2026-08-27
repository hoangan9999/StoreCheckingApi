namespace StoreChecking.Application.Common;

/// <summary>
/// One page of results plus what the client needs to ask for the next one.
/// <para>The property names are part of the HTTP contract the Angular app reads —
/// <c>total</c>, <c>limit</c>, <c>offset</c>, <c>items</c>. Renaming any of them breaks
/// the client, which is why the contract tests assert on them by name.</para>
/// </summary>
public sealed record Paged<T>(int Total, int Limit, int Offset, IReadOnlyList<T> Items);

/// <summary>Shared paging rules, so every listing clamps the same way.</summary>
public static class Page
{
    /// <summary>
    /// Upper bound on how many rows one listing returns. The saved-sentence list is
    /// expected to grow for years of daily practice, so it must not be fetched whole
    /// on every page load.
    /// </summary>
    public const int MaxSize = 200;

    public const int DefaultSize = 50;

    public static int Limit(int? requested) =>
        requested is null or < 1 ? DefaultSize : Math.Min(requested.Value, MaxSize);

    public static int Offset(int? requested) => Math.Max(requested ?? 0, 0);
}
