namespace SmartCraftTask.Dtos;

/// <summary>
/// Payload for POST /warehouse/{warehouseId}/items. The owning warehouse comes from the
/// route, not the body, so the two cannot contradict each other.
/// </summary>
public sealed record CreateItemRequest
{
    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Quantity { get; init; }
}
