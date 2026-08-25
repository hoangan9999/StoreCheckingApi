using Microsoft.EntityFrameworkCore;
using StoreChecking.Api.Auth;
using StoreChecking.Api.Models;

namespace StoreChecking.Api.Data;

public class AppDbContext : DbContext
{
    private readonly CurrentUser _user;

    public AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser user) : base(options)
    {
        _user = user;
    }

    public DbSet<WorkDay> WorkDays => Set<WorkDay>();
    public DbSet<WorkMonthNote> WorkMonthNotes => Set<WorkMonthNote>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<WorkDay>(e =>
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

            // Thay cho RLS: mọi truy vấn tự lọc theo chủ sở hữu, kể cả khi quên viết Where.
            e.HasQueryFilter(x => x.UserId == _user.Id);
        });

        b.Entity<WorkMonthNote>(e =>
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

            e.HasQueryFilter(x => x.UserId == _user.Id);
        });
    }
}
