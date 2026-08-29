using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>A consignment of goods bought in together.</summary>
public class Batch : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    public string Name { get; set; } = "";

    public DateOnly ImportDate { get; set; }

    /// <summary>What the batch cost to buy. Profit is measured against this.</summary>
    public decimal TotalCost { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Which batch to sell from first — 1 is highest. Null means unranked, and unranked
    /// batches sort after every ranked one.
    /// </summary>
    public int? Priority { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One kind of item within a batch.</summary>
public class Product : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    public Guid BatchId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>How many came in. What is left is this minus sold minus damaged.</summary>
    public int Quantity { get; set; }

    public decimal SellPrice { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One line of a sale.
/// <para>A single order can span several batches, in which case its lines share a
/// <see cref="SaleGroupId"/>. Shipping and the note are recorded on the FIRST line of the
/// group only, so that one order is not counted as paying postage several times.</para>
/// </summary>
public class Sale : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    public Guid ProductId { get; set; }
    public Guid BatchId { get; set; }

    public int Quantity { get; set; }

    /// <summary>The price at the time of sale, which may differ from the product's current one.</summary>
    public decimal SellPrice { get; set; }

    /// <summary>Postage paid out of this sale. Subtracted from revenue by both views.</summary>
    public decimal ShippingFee { get; set; }

    public string? Note { get; set; }

    /// <summary>Ties the lines of one multi-batch order together. Null for a single-line sale.</summary>
    public Guid? SaleGroupId { get; set; }

    public DateTimeOffset SoldAt { get; set; }
}

/// <summary>Stock written off as damaged. Reduces what is left without earning anything.</summary>
public class ProductDamage : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    public Guid ProductId { get; set; }
    public Guid BatchId { get; set; }

    public int Quantity { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

// The two roll-ups below are VIEWS. Postgres works out what is left and what was earned —
// including subtracting shipping from revenue and damaged units from stock — so that
// arithmetic is written once instead of in every screen that shows it.

/// <summary>Per-product stock, from the view <c>product_stock</c>.</summary>
public class ProductStock : IOwnedByUser
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BatchId { get; set; }
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public decimal SellPrice { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public long SoldQty { get; set; }
    public long DamagedQty { get; set; }

    /// <summary>Quantity − sold − damaged.</summary>
    public long Remaining { get; set; }

    /// <summary>Σ(quantity × price − shipping) for this product.</summary>
    public decimal Revenue { get; set; }
}

/// <summary>
/// Per-batch roll-up, from the view <c>batch_summary</c>.
/// <para>Note what is NOT here: <c>priority</c>. The view predates that column and was
/// never updated, so anything showing batches has to read it from the table as well.</para>
/// </summary>
public class BatchSummary : IOwnedByUser
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public DateOnly ImportDate { get; set; }
    public decimal TotalCost { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public long ProductCount { get; set; }
    public long TotalQty { get; set; }
    public long SoldQty { get; set; }
    public long DamagedQty { get; set; }
    public long RemainingQty { get; set; }
    public decimal Revenue { get; set; }

    /// <summary>Revenue − total cost.</summary>
    public decimal Profit { get; set; }
}
