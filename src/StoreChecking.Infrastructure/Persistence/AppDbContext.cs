using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using StoreChecking.Application.Abstractions;
using StoreChecking.Domain.Abstractions;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser user) : DbContext(options)
{
    public DbSet<WorkDay> WorkDays => Set<WorkDay>();
    public DbSet<WorkMonthNote> WorkMonthNotes => Set<WorkMonthNote>();
    public DbSet<EnglishWord> EnglishWords => Set<EnglishWord>();
    public DbSet<SavedSentence> SavedSentences => Set<SavedSentence>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<PackingVideo> PackingVideos => Set<PackingVideo>();

    /// <summary>
    /// Read by the generated query filters, once per query.
    /// <para>Must be a member of the context, not a captured value: the EF model is built
    /// once and cached for the whole process, so a filter that closed over one user's id
    /// would hand that user's rows to everybody. EF substitutes the live context when it
    /// runs a filter, which is what makes reading it here correct.</para>
    /// </summary>
    public Guid CurrentUserId => user.Id;

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        ApplyOwnerFilters(b);
    }

    /// <summary>
    /// Gives every <see cref="IOwnedByUser"/> entity the filter that stands in for
    /// Supabase's row level security.
    /// <para>Applied by walking the model rather than written per entity on purpose. A
    /// per-entity line is a thing someone has to remember on every new table, and there
    /// are around fifteen more tables coming. Forgetting one would not fail loudly — it
    /// would quietly serve one user another user's rows. Here there is no line to forget:
    /// implementing the interface IS the filter.</para>
    /// <para>The equivalent of <c>e =&gt; e.UserId == CurrentUserId</c>, built by hand
    /// because the entity type is only known at runtime.</para>
    /// </summary>
    private void ApplyOwnerFilters(ModelBuilder b)
    {
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            if (entityType.IsOwned()) continue;
            if (!typeof(IOwnedByUser).IsAssignableFrom(entityType.ClrType)) continue;

            var row = Expression.Parameter(entityType.ClrType, "e");

            var owner = Expression.Property(row, nameof(IOwnedByUser.UserId));
            var current = Expression.Property(
                Expression.Constant(this),
                nameof(CurrentUserId));

            b.Entity(entityType.ClrType)
                .HasQueryFilter(Expression.Lambda(Expression.Equal(owner, current), row));
        }
    }
}

/// <summary>
/// Design-time factory so `dotnet ef` can build the context without booting the API.
/// <para>Only ever used by tooling: nothing in the running application goes through here,
/// which is why an empty user is fine — no query is executed against this instance.</para>
/// </summary>
public sealed class DesignTimeUser : ICurrentUser
{
    public Guid Id => Guid.Empty;
    public bool IsAnonymous => true;
}
