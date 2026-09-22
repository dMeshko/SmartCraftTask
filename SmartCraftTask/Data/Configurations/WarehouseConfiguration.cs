using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCraftTask.Models;

namespace SmartCraftTask.Data.Configurations;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");

        builder.HasKey(warehouse => warehouse.Id);

        // Ids are assigned in the domain, not by the database.
        builder.Property(warehouse => warehouse.Id)
            .ValueGeneratedNever();

        builder.Property(warehouse => warehouse.Code)
            .HasMaxLength(10)
            .IsRequired();

        // The business identifier: enforced here as well as in the controller, so a race
        // between two concurrent creates still cannot produce a duplicate.
        builder.HasIndex(warehouse => warehouse.Code)
            .IsUnique();

        builder.Property(warehouse => warehouse.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(warehouse => warehouse.IsActive)
            .HasDefaultValue(true);

        // Items is exposed as IReadOnlyCollection, so EF is pointed at the _items field behind it.
        builder.Navigation(warehouse => warehouse.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.ComplexProperty(warehouse => warehouse.Address, address =>
        {
            address.Property(a => a.Street).HasColumnName("Street").HasMaxLength(200).IsRequired();
            address.Property(a => a.PostalCode).HasColumnName("PostalCode").HasMaxLength(20).IsRequired();
            address.Property(a => a.City).HasColumnName("City").HasMaxLength(100).IsRequired();
            address.Property(a => a.Country).HasColumnName("Country").HasMaxLength(100).IsRequired();
        });
    }
}
