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

/// <summary>Label spellings, and which kind each one means.</summary>
public interface ILabelItemAliasRepository
{
    Task<IReadOnlyList<LabelItemAlias>> ListAsync(CancellationToken ct = default);
    Task<LabelItemAlias?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>The alias for one label line, or <c>null</c> when it has never been mapped.</summary>
    Task<LabelItemAlias?> FindByKeyAsync(string sourceName, string variant, CancellationToken ct = default);

    void Add(LabelItemAlias row);
    void Remove(LabelItemAlias row);
}

/// <summary>Which orders have already had their stock taken off.</summary>
public interface ILabelDeductionRepository
{
    Task<LabelDeduction?> FindAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// Just the order codes, so a client can work out what is still owed without asking
    /// about each label one at a time.
    /// <para>Deducting is idempotent, so this is only about not making dozens of pointless
    /// requests; it is never the thing that prevents a second deduction.</para>
    /// </summary>
    Task<IReadOnlyList<string>> ListCodesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LabelDeduction>> ListByGroupAsync(Guid saleGroupId, CancellationToken ct = default);
    void Add(LabelDeduction row);
    void Remove(LabelDeduction row);
}
