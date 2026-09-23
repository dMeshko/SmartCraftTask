namespace SmartCraftTask.Dtos;

/// <summary>
/// Payload for POST /warehouse.
/// </summary>
// Validation rules live in CreateWarehouseRequestValidator.
public sealed record CreateWarehouseRequest
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public AddressDto? Address { get; init; }

    public int CapacityInPallets { get; init; }
}
