using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Configurations;

public sealed class GeneratedPostConfiguration : IEntityTypeConfiguration<GeneratedPost>
{
    public void Configure(EntityTypeBuilder<GeneratedPost> e)
    {
        e.ToTable("generated_posts");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.ImageId).HasColumnName("image_id");
        e.Property(x => x.Title).HasColumnName("title").HasDefaultValue("");
        e.Property(x => x.Content).HasColumnName("content").HasDefaultValue("");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.Error).HasColumnName("error");
        e.Property(x => x.BatchDay).HasColumnName("batch_day").HasColumnType("date");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.Property(x => x.PostedAt).HasColumnName("posted_at");
        e.Property(x => x.FbPostId).HasColumnName("fb_post_id");
        e.Property(x => x.PostError).HasColumnName("post_error");
        e.HasIndex(x => new { x.UserId, x.BatchDay, x.CreatedAt });
    }
}
