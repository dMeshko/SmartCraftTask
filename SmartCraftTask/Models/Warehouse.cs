namespace SmartCraftTask.Models;

/// <summary>
/// Aggregate root. Stock lines belong to this aggregate: they are added, changed and removed
/// through the warehouse that holds them, never on their own. That is why <see cref="Items"/>
/// is read-only here and <see cref="Item"/>'s mutators are internal to the assembly.
/// </summary>
public class Warehouse
{
    private readonly List<Item> _items = [];

    // EF materialises through this; everything else goes through Register.
    private Warehouse()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>Short business identifier (e.g. "OSL-01"). Fixed once assigned.</summary>
    public string Code { get; private init; } = null!;

    public string Name { get; private set; } = null!;

    public Address Address { get; private set; } = null!;

    public int CapacityInPallets { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// SQL Server rowversion, stamped by the database on every write. EF compares it on UPDATE and
    /// DELETE, so a write built from a stale read affects no rows and is reported as a conflict.
    /// </summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Stock held here. Read-only: go through <see cref="AddItem"/> and friends.</summary>
    public IReadOnlyCollection<Item> Items => _items;

    /// <summary>Registers a warehouse, the aggregate generating its own identity and creation stamp.</summary>
    public static Warehouse Register(string code, string name, Address address, int capacityInPallets) =>
        Register(Guid.CreateVersion7(), code, name, address, capacityInPallets, DateTimeOffset.UtcNow);

    /// <summary>
    /// Identity and clock supplied by the caller, for seeding and tests where predictable ids and
    /// timestamps matter more than the aggregate owning them.
    /// </summary>
    internal static Warehouse Register(
        Guid id,
        string code,
        string name,
        Address address,
        int capacityInPallets,
        DateTimeOffset createdAt,
        bool isActive = true) =>
        new()
        {
            Id = id,
            Code = code,
            Name = name,
            Address = address,
            CapacityInPallets = capacityInPallets,
            CreatedAt = createdAt,
            IsActive = isActive
        };

    public void Rename(string name)
    {
        Name = name;
        Touch();
    }

    public void Relocate(Address address)
    {
        Address = address;
        Touch();
    }

    public void Resize(int capacityInPallets)
    {
        if (capacityInPallets < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacityInPallets), capacityInPallets, "Capacity cannot be negative.");
        }

        CapacityInPallets = capacityInPallets;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    /// <summary>
    /// Adds a stock line. The SKU-unique-per-warehouse invariant lives here rather than in the
    /// controller, which is why callers must load <see cref="Items"/> before adding.
    /// </summary>
    public Item AddItem(string sku, string name, int quantity)
    {
        if (!IsActive)
        {
            throw new DomainException(
                "Warehouse is not active",
                $"Warehouse '{Code}' is deactivated and cannot take new stock.");
        }

        if (_items.Any(existing => string.Equals(existing.Sku, sku, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("Duplicate SKU", $"Warehouse '{Code}' already holds SKU '{sku}'.");
        }

        var item = Item.Create(Guid.CreateVersion7(), Id, sku, name, quantity, DateTimeOffset.UtcNow);
        _items.Add(item);

        return item;
    }

    /// <summary>The stock line with this id, or null when the warehouse holds no such line.</summary>
    public Item? FindItem(Guid itemId) => _items.SingleOrDefault(candidate => candidate.Id == itemId);

    /// <summary>Changes a stock line. False when this warehouse holds no such line.</summary>
    public bool TryUpdateItem(Guid itemId, string name, int quantity)
    {
        var item = _items.SingleOrDefault(candidate => candidate.Id == itemId);

        if (item is null)
        {
            return false;
        }

        item.Rename(name);
        item.ChangeQuantity(quantity);

        return true;
    }

    /// <summary>Removes a stock line. False when this warehouse holds no such line.</summary>
    public bool TryRemoveItem(Guid itemId)
    {
        var item = _items.SingleOrDefault(candidate => candidate.Id == itemId);

        return item is not null && _items.Remove(item);
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
