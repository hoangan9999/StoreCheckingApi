using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Abstractions;

/// <summary>The album the daily video job draws from.</summary>
public interface IMediaImageRepository
{
    /// <summary>One page of the album, newest upload first.</summary>
    Task<(int Total, IReadOnlyList<MediaImage> Items)> ListAsync(
        DateOnly? day, int skip, int take, CancellationToken ct = default);

    /// <summary>How many pictures were uploaded on each day — the album's date index.</summary>
    Task<IReadOnlyList<(DateOnly Day, int Count)>> CountByDayAsync(CancellationToken ct = default);

    /// <summary>
    /// Pictures for one video: least used first, and among equals the one left alone longest.
    /// <para>Not random. Random lets the five videos of a single day repeat each other while
    /// older pictures are never chosen at all — the album grows and most of it is dead
    /// weight. This spreads the work across everything that was uploaded.</para>
    /// </summary>
    Task<IReadOnlyList<MediaImage>> PickLeastUsedAsync(int count, CancellationToken ct = default);

    Task<MediaImage?> FindAsync(Guid id, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>
    /// Whose album this is, worked out from the data itself.
    /// <para>The nightly job has no request behind it and therefore no token to read an
    /// owner from. Asking the data beats putting the id in configuration: nothing to fill
    /// in by hand, and nothing left pointing at the wrong account after a re-login.</para>
    /// <para>The ONLY place that steps outside the owner filter, and it returns an id
    /// rather than rows — no caller can reach another person's data through it.</para>
    /// </summary>
    Task<Guid?> FindOwnerAsync(CancellationToken ct = default);

    void Add(MediaImage row);
    void Remove(MediaImage row);
}

/// <summary>Videos the daily job produced.</summary>
public interface IGeneratedVideoRepository
{
    Task<(int Total, IReadOnlyList<GeneratedVideo> Items)> ListAsync(
        DateOnly? day, int skip, int take, CancellationToken ct = default);

    Task<IReadOnlyList<(DateOnly Day, int Count)>> CountByDayAsync(CancellationToken ct = default);

    /// <summary>How many were already built for a day — what stops a second batch being made.</summary>
    Task<int> CountForDayAsync(DateOnly day, CancellationToken ct = default);

    Task<GeneratedVideo?> FindAsync(Guid id, CancellationToken ct = default);

    void Add(GeneratedVideo row);
    void Remove(GeneratedVideo row);
}

/// <summary>
/// Where picture and video files actually live.
///
/// <para>Behind an interface so nothing above Infrastructure knows there is a disk. It also
/// keeps every path decision in ONE place: a filename that arrived from outside must never
/// be able to point at `../../etc`, and one implementation is far easier to be sure of than
/// a check repeated at every call site.</para>
/// </summary>
public interface IMediaStorage
{
    /// <summary>Saves bytes under a generated name and returns that name.</summary>
    Task<string> SaveImageAsync(Stream content, string extension, CancellationToken ct = default);

    Task<string> SaveVideoAsync(Stream content, string extension, CancellationToken ct = default);

    /// <summary>Opens a stored file, or null when it is not there.</summary>
    Stream? OpenImage(string filename);
    Stream? OpenVideo(string filename);

    /// <summary>Full path of a stored picture — ffmpeg needs real paths, not streams.</summary>
    string? ImagePath(string filename);
    string VideoPath(string filename);

    void DeleteImage(string filename);
    void DeleteVideo(string filename);
}
