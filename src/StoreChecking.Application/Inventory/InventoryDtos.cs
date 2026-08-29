namespace StoreChecking.Application.Inventory;

// ---------- Batches ----------

/// <summary>
/// A batch with its roll-up.
/// <para>Carries <c>Priority</c>, which the <c>batch_summary</c> view does not have. The
/// client used to fetch the view and the table separately and merge them; the merge
/// happens here instead, so a screen costs one call rather than two.</para>
/// </summary>
public record BatchSummaryDto(
    Guid Id, string Name, string ImportDate, decimal TotalCost, string? Note, int? Priority,
    DateTimeOffset CreatedAt,
    long ProductCount, long TotalQty, long SoldQty, long DamagedQty, long RemainingQty,
    decimal Revenue, decimal Profit);

/// <summary>One product row when creating a batch.</summary>
public record NewProductRow(string Name, int? Quantity, decimal? SellPrice);

/// <summary>Create a batch and the products in it, in one go.</summary>
public record CreateBatchRequest(
    string Name, string ImportDate, decimal TotalCost, string? Note, IReadOnlyList<NewProductRow>? Products);

/// <summary>Edit a batch. Full replacement.</summary>
public record UpdateBatchRequest(
    string Name, string ImportDate, decimal TotalCost, string? Note, int? Priority);

/// <summary>One entry of a drag-and-drop reorder.</summary>
public record BatchPriority(Guid Id, int Priority);

/// <summary>Reorder several batches at once.</summary>
public record SetPrioritiesRequest(IReadOnlyList<BatchPriority> Items);

// ---------- Products ----------

/// <summary>A product together with what is left of it.</summary>
public record ProductStockDto(
    Guid Id, Guid BatchId, string Name, int Quantity, decimal SellPrice, DateTimeOffset CreatedAt,
    long SoldQty, long DamagedQty, long Remaining, decimal Revenue);

/// <summary>Create or replace one product.</summary>
public record SaveProductRequest(string Name, int Quantity, decimal SellPrice);

/// <summary>A product still in stock, ready to be picked when selling.</summary>
public record StockItemDto(
    Guid Id, string Name, Guid BatchId, string BatchName, int? BatchPriority,
    decimal SellPrice, long Remaining);

// ---------- Sales ----------

/// <summary>One line of a sale.</summary>
public record SaleDto(
    Guid Id, Guid ProductId, Guid BatchId, int Quantity, decimal SellPrice, decimal ShippingFee,
    string? Note, Guid? SaleGroupId, DateTimeOffset SoldAt);

/// <summary>A sale line with the product and batch named, for the global history.</summary>
public record SaleRowDto(
    Guid Id, Guid ProductId, Guid BatchId, int Quantity, decimal SellPrice, decimal ShippingFee,
    string? Note, Guid? SaleGroupId, DateTimeOffset SoldAt,
    string ProductName, string BatchName, int? BatchPriority);

/// <summary>
/// One page of sales history.
/// <para><c>Total</c> counts ORDERS and <c>TotalAmount</c> sums the whole range, not the
/// page — that is what keeps "N đơn" and the revenue figure honest while the list itself
/// only holds what has been scrolled to.</para>
/// </summary>
public record SalesPageDto(
    int Total, decimal TotalAmount, int Limit, int Offset, IReadOnlyList<SaleRowDto> Items);

/// <summary>One item of an order.</summary>
public record SaleItem(Guid ProductId, int Quantity, decimal SellPrice);

/// <summary>
/// Record an order, which may span several batches.
/// <para>Shipping and the note belong to the ORDER, not to a line, so they are written on
/// the first line only — otherwise a two-line order would look like it paid postage
/// twice, and revenue would come out short.</para>
/// </summary>
public record RecordSaleRequest(
    IReadOnlyList<SaleItem> Items, DateTimeOffset SoldAt, decimal ShippingFee, string? Note);

/// <summary>Edit one sale line.</summary>
public record UpdateSaleRequest(
    int Quantity, decimal SellPrice, DateTimeOffset SoldAt, decimal ShippingFee, string? Note);

// ---------- Damages ----------

/// <summary>Stock written off as damaged.</summary>
public record DamageDto(Guid Id, Guid ProductId, Guid BatchId, int Quantity, string? Note, DateTimeOffset CreatedAt);

/// <summary>Write off some stock as damaged.</summary>
public record AddDamageRequest(Guid ProductId, int Quantity, string? Note);
