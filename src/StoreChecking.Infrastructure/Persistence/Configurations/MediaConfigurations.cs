using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Configurations;

public sealed class MediaImageConfiguration : IEntityTypeConfiguration<MediaImage>
{
    public void Configure(EntityTypeBuilder<MediaImage> e)
    {
        e.ToTable("media_images");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Filename).HasColumnName("filename");
        e.Property(x => x.OriginalName).HasColumnName("original_name").HasDefaultValue("");
        e.Property(x => x.ContentType).HasColumnName("content_type").HasDefaultValue("image/jpeg");
        e.Property(x => x.Bytes).HasColumnName("bytes").HasDefaultValue(0L);
        e.Property(x => x.Width).HasColumnName("width");
        e.Property(x => x.Height).HasColumnName("height");
        e.Property(x => x.UseCount).HasColumnName("use_count").HasDefaultValue(0);
        e.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        e.Property(x => x.UploadedAt).HasColumnName("uploaded_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.UploadedAt });
    }
}

public sealed class GeneratedVideoConfiguration : IEntityTypeConfiguration<GeneratedVideo>
{
    public void Configure(EntityTypeBuilder<GeneratedVideo> e)
    {
        e.ToTable("generated_videos");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Filename).HasColumnName("filename");
        e.Property(x => x.Title).HasColumnName("title").HasDefaultValue("");
        e.Property(x => x.Script).HasColumnName("script").HasDefaultValue("");
        e.Property(x => x.DurationSec).HasColumnName("duration_sec");
        e.Property(x => x.Bytes).HasColumnName("bytes");
        e.Property(x => x.Status).HasColumnName("status").HasDefaultValue(VideoStatus.Pending);
        e.Property(x => x.Error).HasColumnName("error");
        // uuid[] natively — Npgsql maps a Guid array straight onto it, no join table and no
        // JSON blob for something that is only ever read back whole.
        e.Property(x => x.ImageIds).HasColumnName("image_ids").HasColumnType("uuid[]");
        e.Property(x => x.BatchDay).HasColumnName("batch_day").HasColumnType("date");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.Property(x => x.FinishedAt).HasColumnName("finished_at");
        e.HasIndex(x => new { x.UserId, x.BatchDay });
    }
}
