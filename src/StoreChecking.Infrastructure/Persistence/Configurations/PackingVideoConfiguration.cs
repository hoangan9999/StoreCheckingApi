using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Configurations;

public sealed class PackingVideoConfiguration : IEntityTypeConfiguration<PackingVideo>
{
    public void Configure(EntityTypeBuilder<PackingVideo> e)
    {
        e.ToTable("packing_videos");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.OrderCode).HasColumnName("order_code");
        e.Property(x => x.Seq).HasColumnName("seq");
        e.Property(x => x.Note).HasColumnName("note");
        e.Property(x => x.Filename).HasColumnName("filename");
        e.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasDefaultValueSql("now()");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.OrderCode });
        e.HasIndex(x => new { x.UserId, x.RecordedAt });
    }
}
