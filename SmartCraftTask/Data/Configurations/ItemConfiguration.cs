using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCraftTask.Models;

namespace SmartCraftTask.Data.Configurations;

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("Items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .ValueGeneratedNever();

        builder.Property(item => item.Sku)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(item => item.Name)
            .HasMaxLength(100)
            .IsRequired();

        // A SKU identifies a line within one warehouse, so the uniqueness is composite.
        builder.HasIndex(item => new { item.WarehouseId, item.Sku })
            .IsUnique();

        // Kept in step by the database, so a single UPDATE to Quantity cannot leave the two
        // disagreeing, and ?isOnStock= can be answered in SQL instead of in memory.
        builder.Property(item => item.IsOnStock)
            .HasComputedColumnSql("CASE WHEN [Quantity] > 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END", stored: true);

        builder.HasOne(item => item.Warehouse)
            .WithMany(warehouse => warehouse.Items)
            .HasForeignKey(item => item.WarehouseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
