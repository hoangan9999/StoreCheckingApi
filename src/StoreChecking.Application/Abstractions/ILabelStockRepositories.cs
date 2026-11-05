using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Abstractions;

/// <summary>Kinds — the stable identity behind the product rows of each batch.</summary>
public interface IProductKindRepository
{
    Task<IReadOnlyList<ProductKind>> ListAsync(CancellationToken ct = default);
    Task<ProductKind?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Find by normalised name. The ONLY lookup by text in the whole feature, and it
    /// exists so that typing an existing product's name again does not create a second
    /// kind for it. Nothing else may resolve a kind this way.
    /// </summary>
    Task<ProductKind?> FindByKeyAsync(string nameKey, CancellationToken ct = default);

    void Add(ProductKind row);
}

/// <summary>
/// What a label wording meant LAST time somebody deducted it.
/// <para>A suggestion, and only a suggestion. Nothing acts on these: they pre-select the
/// dropdown so twenty identical orders are not twenty identical decisions, and every one
/// of those twenty still has to be confirmed by hand.</para>
/// </summary>
public interface ILabelItemAliasRepository
{
    Task<IReadOnlyList<LabelItemAlias>> ListAsync(CancellationToken ct = default);

    /// <summary>The suggestion for one label line, or <c>null</c> when it is new.</summary>
    Task<LabelItemAlias?> FindByKeyAsync(string sourceName, string variant, CancellationToken ct = default);

    void Add(LabelItemAlias row);
}

/// <summary>Which labels have been dealt with, and how.</summary>
public interface ILabelStockActionRepository
{
    Task<LabelStockAction?> FindAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// Every order code with a decision on it, at any age.
    /// <para>This is what turns "every label on the NAS" into "the labels still waiting",
    /// so it must not be limited by date: a code missing from here means untouched, and a
    /// windowed list would quietly resurrect last month's work.</para>
    /// </summary>
    Task<IReadOnlyList<string>> ListCodesAsync(CancellationToken ct = default);

    /// <summary>Decisions made since <paramref name="since"/>, newest first — the undo list.</summary>
    Task<IReadOnlyList<LabelStockAction>> ListSinceAsync(
        DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Finds the decision that produced a set of sale lines, so deleting them can clear it.</summary>
    Task<IReadOnlyList<LabelStockAction>> ListByGroupAsync(
        Guid saleGroupId, CancellationToken ct = default);

    void Add(LabelStockAction row);
    void Remove(LabelStockAction row);
    void RemoveRange(IEnumerable<LabelStockAction> rows);
}
