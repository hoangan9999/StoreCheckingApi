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

public sealed class EnglishTopicConfiguration : IEntityTypeConfiguration<EnglishTopic>
{
    public void Configure(EntityTypeBuilder<EnglishTopic> e)
    {
        e.ToTable("english_topics");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Title).HasColumnName("title");
        e.Property(x => x.Position).HasColumnName("position");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.Position });
    }
}

public sealed class EnglishLessonLineConfiguration : IEntityTypeConfiguration<EnglishLessonLine>
{
    public void Configure(EntityTypeBuilder<EnglishLessonLine> e)
    {
        e.ToTable("english_lesson_lines");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.TopicId).HasColumnName("topic_id");
        e.Property(x => x.Kind).HasColumnName("kind");
        e.Property(x => x.Speaker).HasColumnName("speaker");
        e.Property(x => x.En).HasColumnName("en");
        e.Property(x => x.Vi).HasColumnName("vi");
        e.Property(x => x.Position).HasColumnName("position");
        e.HasIndex(x => new { x.TopicId, x.Position });

        // Khai quan hệ dù không dùng navigation property nào. Thiếu nó thì EF KHÔNG biết
        // topic_id là khoá ngoại, nên khi lưu một chủ đề mới cùng các dòng của nó trong
        // một lượt, EF có thể chèn dòng trước chủ đề — và database từ chối. Đây là thứ
        // chỉ hỏng ở đường thành công, nên mọi test kiểm tra đầu vào vẫn xanh.
        e.HasOne<EnglishTopic>()
            .WithMany()
            .HasForeignKey(x => x.TopicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
