using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Configurations;

// Column names are spelled out rather than left to a naming convention: the tables were
// created by db/*.sql, ported from Supabase, and the mapping has to follow them exactly.
//
// No HasQueryFilter anywhere in these files. The owner filter is applied to every
// IOwnedByUser entity by AppDbContext, so there is nothing here to leave out by mistake.

public sealed class WorkDayConfiguration : IEntityTypeConfiguration<WorkDay>
{
    public void Configure(EntityTypeBuilder<WorkDay> e)
    {
        e.ToTable("work_days");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Day).HasColumnName("day");
        e.Property(x => x.Note).HasColumnName("note").HasDefaultValue("");
        e.Property(x => x.Color).HasColumnName("color");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.Day }).IsUnique();
    }
}

public sealed class WorkMonthNoteConfiguration : IEntityTypeConfiguration<WorkMonthNote>
{
    public void Configure(EntityTypeBuilder<WorkMonthNote> e)
    {
        e.ToTable("work_month_notes");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Period).HasColumnName("period");
        e.Property(x => x.Content).HasColumnName("content").HasDefaultValue("");
        e.Property(x => x.Sort).HasColumnName("sort");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.Period, x.Sort });
    }
}
