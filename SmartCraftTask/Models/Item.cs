namespace SmartCraftTask.Models;

/// <summary>
/// A stock line inside a <see cref="Warehouse"/> aggregate. Created and changed only through its
/// root: the mutators are internal, so no controller can reach past the warehouse to touch one.
/// </summary>
public class Item
{
    private Item()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>Stock-keeping unit, unique within its warehouse rather than globally. Fixed once assigned.</summary>
    public string Sku { get; private init; } = null!;

    public string Name { get; private set; } = null!;

    public int Quantity { get; private set; }

    /// <summary>Derived from <see cref="Quantity"/> by the database, never assigned in code.</summary>
    public bool IsOnStock { get; private set; }

    public Guid WarehouseId { get; private init; }

    public Warehouse? Warehouse { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    internal static Item Create(
        Guid id,
        Guid warehouseId,
        string sku,
        string name,
        int quantity,
        DateTimeOffset createdAt)
    {
        GuardQuantity(quantity);

        return new Item
        {
            Id = id,
            WarehouseId = warehouseId,
            Sku = sku,
            Name = name,
            Quantity = quantity,
            CreatedAt = createdAt
        };
    }

    internal void Rename(string name)
    {
        Name = name;
        Touch();
    }

    internal void ChangeQuantity(int quantity)
    {
        GuardQuantity(quantity);

        Quantity = quantity;
        Touch();
    }

    private static void GuardQuantity(int quantity)
    {
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity cannot be negative.");
        }
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
