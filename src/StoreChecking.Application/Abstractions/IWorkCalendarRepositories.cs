using StoreChecking.Domain.Entities;

namespace StoreChecking.Application.Abstractions;

/// <summary>
/// Day cells of the work calendar.
/// <para>Every method here is already scoped to the current user: the implementation sits
/// on a DbContext whose global query filters do that. A repository method that reaches
/// around those filters is a data leak, so none of them do.</para>
/// </summary>
public interface IWorkDayRepository
{
    Task<IReadOnlyList<WorkDay>> ListRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<WorkDay?> FindByDayAsync(DateOnly day, CancellationToken ct = default);
    void Add(WorkDay row);
    void Remove(WorkDay row);
}

/// <summary>Free-text note lines attached to a whole month.</summary>
public interface IWorkMonthNoteRepository
{
    Task<IReadOnlyList<WorkMonthNote>> ListByPeriodAsync(DateOnly period, CancellationToken ct = default);
    Task<WorkMonthNote?> FindAsync(Guid id, CancellationToken ct = default);
    void Add(WorkMonthNote row);
    void Remove(WorkMonthNote row);
}
