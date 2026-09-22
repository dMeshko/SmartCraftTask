namespace SmartCraftTask.Dtos;

/// <summary>
/// The read model returned by every item endpoint.
/// </summary>
public sealed record ItemResponse
{
    public Guid Id { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Quantity { get; init; }

    /// <summary>Whether this line currently holds stock. Derived from Quantity; read-only.</summary>
    public bool IsOnStock { get; init; }

    public Guid WarehouseId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
