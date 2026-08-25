namespace StoreChecking.Api.Models;

/// <summary>
/// One day cell in the work calendar. A row exists only when the cell has a note
/// or a colour — empty cells are deleted rather than stored.
/// <para><c>Color</c> holds a colour KEY ('vang', 'luc', …), not a hex code, so the
/// Angular side can render it correctly in both light and dark themes.</para>
/// </summary>
public class WorkDay
{
    public Guid Id { get; set; }

    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    /// <summary>The calendar day (date only, no time component).</summary>
    public DateOnly Day { get; set; }

    public string Note { get; set; } = "";

    public string? Color { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
