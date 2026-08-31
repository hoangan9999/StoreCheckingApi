using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Configurations;

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> e)
    {
        e.ToTable("app_settings");

        // Composite key: one row per person per setting, and the owner is half of it, so a
        // second person's copy of the same switch can never collide with the first's.
        e.HasKey(x => new { x.UserId, x.Key });

        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Key).HasColumnName("key");
        e.Property(x => x.Value).HasColumnName("value");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}
