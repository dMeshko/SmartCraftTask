namespace SmartCraftTask.Dtos;

/// <summary>
/// Payload for PUT /warehouse/{warehouseId}/items/{id}. Sku is absent for the same reason
/// warehouse Code is: it identifies the line and stays fixed once assigned.
/// </summary>
public sealed record UpdateItemRequest
{
    public string Name { get; init; } = string.Empty;

    public int Quantity { get; init; }
}
