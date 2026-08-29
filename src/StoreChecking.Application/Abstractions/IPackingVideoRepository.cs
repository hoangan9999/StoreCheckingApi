using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Abstractions;

/// <summary>Packing recordings, newest first.</summary>
public interface IPackingVideoRepository
{
    /// <param name="query">Matches the order code, case-insensitively. Null or blank returns everything.</param>
    Task<IReadOnlyList<PackingVideo>> SearchAsync(string? query, int take, CancellationToken ct = default);

    /// <summary>
    /// The sequence number to give the next recording of this order.
    /// <para>Derived from the highest existing seq, NOT from a row count. A count goes down
    /// when a recording is deleted, which would hand the next upload a file name that
    /// already exists on the NAS and overwrite a video that is still referenced.</para>
    /// </summary>
    Task<int> NextSeqAsync(string orderCode, CancellationToken ct = default);

    Task<PackingVideo?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Every file name already logged, for deciding what on the NAS is new.</summary>
    Task<IReadOnlyList<string>> FilenamesAsync(CancellationToken ct = default);

    void Add(PackingVideo row);
    void AddRange(IEnumerable<PackingVideo> rows);
    void Remove(PackingVideo row);
}
