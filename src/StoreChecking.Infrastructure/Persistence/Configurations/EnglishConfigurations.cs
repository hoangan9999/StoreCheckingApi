using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Configurations;

public sealed class EnglishWordConfiguration : IEntityTypeConfiguration<EnglishWord>
{
    public void Configure(EntityTypeBuilder<EnglishWord> e)
    {
        e.ToTable("english_words");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Word).HasColumnName("word");
        e.Property(x => x.Data).HasColumnName("data").HasColumnType("jsonb");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}

public sealed class SavedSentenceConfiguration : IEntityTypeConfiguration<SavedSentence>
{
    public void Configure(EntityTypeBuilder<SavedSentence> e)
    {
        e.ToTable("speaking_saved");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Text).HasColumnName("text");
        e.Property(x => x.Note).HasColumnName("note").HasDefaultValue("");
        e.Property(x => x.Context).HasColumnName("context").HasDefaultValue("");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}
