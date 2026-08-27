using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Repositories;

// None of these call IgnoreQueryFilters(). That is the one thing that would undo the
// owner filtering the whole design rests on, so it appears nowhere in this folder.

public sealed class WorkDayRepository(AppDbContext db) : IWorkDayRepository
{
    public async Task<IReadOnlyList<WorkDay>> ListRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await db.WorkDays
            .Where(x => x.Day >= from && x.Day <= to)
            .OrderBy(x => x.Day)
            .ToListAsync(ct);

    public Task<WorkDay?> FindByDayAsync(DateOnly day, CancellationToken ct = default) =>
        db.WorkDays.FirstOrDefaultAsync(x => x.Day == day, ct);

    public void Add(WorkDay row) => db.WorkDays.Add(row);
    public void Remove(WorkDay row) => db.WorkDays.Remove(row);
}

public sealed class WorkMonthNoteRepository(AppDbContext db) : IWorkMonthNoteRepository
{
    public async Task<IReadOnlyList<WorkMonthNote>> ListByPeriodAsync(DateOnly period, CancellationToken ct = default) =>
        await db.WorkMonthNotes
            .Where(x => x.Period == period)
            .OrderBy(x => x.Sort).ThenBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public Task<WorkMonthNote?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.WorkMonthNotes.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(WorkMonthNote row) => db.WorkMonthNotes.Add(row);
    public void Remove(WorkMonthNote row) => db.WorkMonthNotes.Remove(row);
}
