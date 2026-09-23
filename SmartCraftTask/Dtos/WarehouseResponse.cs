namespace SmartCraftTask.Dtos;

/// <summary>
/// The read model returned by every warehouse endpoint.
/// </summary>
public sealed record WarehouseResponse
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public AddressDto Address { get; init; } = new();

    public int CapacityInPallets { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Concurrency token. The same value the ETag header carries, base64 encoded.</summary>
    public byte[] RowVersion { get; init; } = [];

    /// <summary>Number of stock lines held in this warehouse.</summary>
    public int ItemCount { get; init; }
}
