namespace SmartCraftTask.Dtos;

/// <summary>
/// Payload for PUT /warehouse/{id}. Code is deliberately absent: it is the warehouse's
/// business identifier and stays fixed once assigned.
/// </summary>
// Validation rules live in UpdateWarehouseRequestValidator.
public sealed record UpdateWarehouseRequest
{
    public string Name { get; init; } = string.Empty;

    public AddressDto? Address { get; init; }

    public int CapacityInPallets { get; init; }

    public bool IsActive { get; init; } = true;
}
