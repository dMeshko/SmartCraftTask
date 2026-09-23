using SmartCraftTask.Models;

namespace SmartCraftTask.Tests;

/// <summary>
/// The point of moving the invariants into the aggregate: they can be tested without a database,
/// a web host, or a mapper.
/// </summary>
public class WarehouseAggregateTests
{
    [Fact]
    public void AddItem_refuses_a_sku_the_warehouse_already_holds()
    {
        var warehouse = NewWarehouse();
        warehouse.AddItem("PAL-1", "Oak plank", quantity: 5);

        var exception = Assert.Throws<DomainException>(() => warehouse.AddItem("PAL-1", "Oak plank again", 3));

        Assert.Equal("Duplicate SKU", exception.Title);
        Assert.Single(warehouse.Items);
    }

    [Fact]
    public void AddItem_matches_skus_case_insensitively()
    {
        var warehouse = NewWarehouse();
        warehouse.AddItem("PAL-1", "Oak plank", quantity: 5);

        Assert.Throws<DomainException>(() => warehouse.AddItem("pal-1", "Same thing, lower case", 3));
    }

    [Fact]
    public void AddItem_refuses_stock_for_a_deactivated_warehouse()
    {
        var warehouse = NewWarehouse();
        warehouse.Deactivate();

        var exception = Assert.Throws<DomainException>(() => warehouse.AddItem("PAL-1", "Oak plank", 5));

        Assert.Equal("Warehouse is not active", exception.Title);
        Assert.Empty(warehouse.Items);
    }

    [Fact]
    public void AddItem_accepts_a_sku_again_once_the_previous_line_is_removed()
    {
        var warehouse = NewWarehouse();
        var item = warehouse.AddItem("PAL-1", "Oak plank", quantity: 5);

        Assert.True(warehouse.TryRemoveItem(item.Id));
        warehouse.AddItem("PAL-1", "Oak plank, restocked", quantity: 2);

        Assert.Single(warehouse.Items);
        Assert.Equal(2, warehouse.Items.Single().Quantity);
    }

    [Fact]
    public void TryUpdateItem_reports_an_unknown_item_rather_than_throwing()
    {
        var warehouse = NewWarehouse();

        Assert.False(warehouse.TryUpdateItem(Guid.NewGuid(), "Nothing", 1));
    }

    [Fact]
    public void Quantity_cannot_go_negative()
    {
        var warehouse = NewWarehouse();
        var item = warehouse.AddItem("PAL-1", "Oak plank", quantity: 5);

        Assert.Throws<ArgumentOutOfRangeException>(() => warehouse.TryUpdateItem(item.Id, "Oak plank", -1));
        Assert.Equal(5, item.Quantity);
    }

    [Fact]
    public void Renaming_stamps_UpdatedAt_but_leaves_the_code_alone()
    {
        var warehouse = NewWarehouse();
        Assert.Null(warehouse.UpdatedAt);

        warehouse.Rename("Oslo Central, renamed");

        Assert.Equal("Oslo Central, renamed", warehouse.Name);
        Assert.Equal("OSL-01", warehouse.Code);
        Assert.NotNull(warehouse.UpdatedAt);
    }

    private static Warehouse NewWarehouse() =>
        Warehouse.Register(
            "OSL-01",
            "Oslo Central",
            new Address { Street = "Karl Johans gate 1", PostalCode = "0154", City = "Oslo", Country = "Norway" },
            capacityInPallets: 1200);
}
