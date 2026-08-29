using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreChecking.Domain.Entities;

namespace StoreChecking.Infrastructure.Persistence.Configurations;

public sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> e)
    {
        e.ToTable("batches");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Name).HasColumnName("name");
        e.Property(x => x.ImportDate).HasColumnName("import_date").HasDefaultValueSql("current_date");
        e.Property(x => x.TotalCost).HasColumnName("total_cost").HasColumnType("numeric(14,2)").HasDefaultValue(0m);
        e.Property(x => x.Note).HasColumnName("note");
        e.Property(x => x.Priority).HasColumnName("priority");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.UserId, x.Priority });
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> e)
    {
        e.ToTable("products");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.BatchId).HasColumnName("batch_id");
        e.Property(x => x.Name).HasColumnName("name");
        e.Property(x => x.Quantity).HasColumnName("quantity");
        e.Property(x => x.SellPrice).HasColumnName("sell_price").HasColumnType("numeric(14,2)");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.HasIndex(x => x.BatchId);
    }
}

public sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> e)
    {
        e.ToTable("sales");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.ProductId).HasColumnName("product_id");
        e.Property(x => x.BatchId).HasColumnName("batch_id");
        e.Property(x => x.Quantity).HasColumnName("quantity");
        e.Property(x => x.SellPrice).HasColumnName("sell_price").HasColumnType("numeric(14,2)");
        e.Property(x => x.ShippingFee).HasColumnName("shipping_fee").HasColumnType("numeric(14,2)").HasDefaultValue(0m);
        e.Property(x => x.Note).HasColumnName("note");
        e.Property(x => x.SaleGroupId).HasColumnName("sale_group_id");
        e.Property(x => x.SoldAt).HasColumnName("sold_at").HasDefaultValueSql("now()");
        e.HasIndex(x => x.ProductId);
        e.HasIndex(x => x.BatchId);
        e.HasIndex(x => x.SaleGroupId);
    }
}

public sealed class ProductDamageConfiguration : IEntityTypeConfiguration<ProductDamage>
{
    public void Configure(EntityTypeBuilder<ProductDamage> e)
    {
        e.ToTable("product_damages");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.ProductId).HasColumnName("product_id");
        e.Property(x => x.BatchId).HasColumnName("batch_id");
        e.Property(x => x.Quantity).HasColumnName("quantity");
        e.Property(x => x.Note).HasColumnName("note");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        e.HasIndex(x => x.ProductId);
        e.HasIndex(x => x.BatchId);
    }
}

// The two stock roll-ups, keyless and read-only. Postgres subtracts shipping from revenue
// and damaged units from what is left, so that arithmetic is written once.

public sealed class ProductStockConfiguration : IEntityTypeConfiguration<ProductStock>
{
    public void Configure(EntityTypeBuilder<ProductStock> e)
    {
        e.HasNoKey().ToView("product_stock");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.BatchId).HasColumnName("batch_id");
        e.Property(x => x.Name).HasColumnName("name");
        e.Property(x => x.Quantity).HasColumnName("quantity");
        e.Property(x => x.SellPrice).HasColumnName("sell_price");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.SoldQty).HasColumnName("sold_qty");
        e.Property(x => x.DamagedQty).HasColumnName("damaged_qty");
        e.Property(x => x.Remaining).HasColumnName("remaining");
        e.Property(x => x.Revenue).HasColumnName("revenue");
    }
}

public sealed class BatchSummaryConfiguration : IEntityTypeConfiguration<BatchSummary>
{
    public void Configure(EntityTypeBuilder<BatchSummary> e)
    {
        e.HasNoKey().ToView("batch_summary");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.Name).HasColumnName("name");
        e.Property(x => x.ImportDate).HasColumnName("import_date");
        e.Property(x => x.TotalCost).HasColumnName("total_cost");
        e.Property(x => x.Note).HasColumnName("note");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.ProductCount).HasColumnName("product_count");
        e.Property(x => x.TotalQty).HasColumnName("total_qty");
        e.Property(x => x.SoldQty).HasColumnName("sold_qty");
        e.Property(x => x.DamagedQty).HasColumnName("damaged_qty");
        e.Property(x => x.RemainingQty).HasColumnName("remaining_qty");
        e.Property(x => x.Revenue).HasColumnName("revenue");
        e.Property(x => x.Profit).HasColumnName("profit");
    }
}
