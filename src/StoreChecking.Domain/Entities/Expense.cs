using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>One thing that was paid for.</summary>
public class Expense : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The category this was spent on. The database refuses to delete a category that still
    /// has spending against it (<c>on delete restrict</c>), because losing the category
    /// would lose the record of where the money went.
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>The day it was spent — a date, with no time and no timezone.</summary>
    public DateOnly SpentOn { get; set; }

    public string? Description { get; set; }

    public decimal Amount { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
