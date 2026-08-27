using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoreChecking.Application.Abstractions;
using StoreChecking.Application.English;
using StoreChecking.Application.WorkCalendar;
using StoreChecking.Infrastructure.Persistence;
using StoreChecking.Infrastructure.Persistence.Repositories;

namespace StoreChecking.Infrastructure;

/// <summary>
/// One place that knows how the layers are wired, so Program.cs does not.
/// <para>Every new module adds its repositories and its service here. Keeping that in
/// Infrastructure rather than in the API means the API project never has to name a
/// concrete repository — it only ever sees the interfaces from Application.</para>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDatabaseHealth, DatabaseHealth>();

        // ---------- Repositories ----------
        services.AddScoped<IWorkDayRepository, WorkDayRepository>();
        services.AddScoped<IWorkMonthNoteRepository, WorkMonthNoteRepository>();
        services.AddScoped<IEnglishWordRepository, EnglishWordRepository>();
        services.AddScoped<ISavedSentenceRepository, SavedSentenceRepository>();

        // ---------- Application services ----------
        // Registered here rather than in a separate AddApplication(): they have no
        // configuration of their own, and one list is easier to keep complete than two.
        services.AddScoped<WorkCalendarService>();
        services.AddScoped<EnglishService>();

        return services;
    }
}
