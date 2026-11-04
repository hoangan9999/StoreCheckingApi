using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Configurations;

public sealed class ProductKindConfiguration : IEntityTypeConfiguration<ProductKind>
{
    public void Configure(EntityTypeBuilder<ProductKind> e)
    {
        e.ToTable("product_kinds");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Name).HasColumnName("name");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.HasIndex(x => x.UserId);
    }
}

public sealed class LabelItemAliasConfiguration : IEntityTypeConfiguration<LabelItemAlias>
{
    public void Configure(EntityTypeBuilder<LabelItemAlias> e)
    {
        e.ToTable("label_item_aliases");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.SourceName).HasColumnName("source_name");
        e.Property(x => x.Variant).HasColumnName("variant");
        e.Property(x => x.Display).HasColumnName("display");
        e.Property(x => x.KindId).HasColumnName("kind_id");
        e.Property(x => x.Units).HasColumnName("units").HasDefaultValue(1);
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.SourceName, x.Variant }).IsUnique();
        e.HasIndex(x => x.KindId);
    }
}

public sealed class LabelDeductionConfiguration : IEntityTypeConfiguration<LabelDeduction>
{
    public void Configure(EntityTypeBuilder<LabelDeduction> e)
    {
        e.ToTable("label_deductions");
        // The composite key IS the guard against deducting twice, so it is the primary key
        // rather than a surrogate id with a unique index beside it.
        e.HasKey(x => new { x.UserId, x.OrderCode });
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.OrderCode).HasColumnName("order_code");
        e.Property(x => x.SaleGroupId).HasColumnName("sale_group_id");
        e.Property(x => x.UnitsTotal).HasColumnName("units_total");
        e.Property(x => x.DeductedAt).HasColumnName("deducted_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.SaleGroupId });
    }
}
