namespace SmartCraftTask.Dtos;

/// <summary>
/// Address as it travels over the wire, both inbound and outbound.
/// </summary>
public sealed record AddressDto
{
    public string Street { get; init; } = string.Empty;

    public string PostalCode { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;
}
