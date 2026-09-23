namespace SmartCraftTask.Models;

/// <summary>
/// Where a <see cref="Warehouse"/> can be found. A value object: replaced wholesale, never edited in place.
/// Mapped as an EF complex type, so it lives in the warehouse's own table without an identity of its own.
/// </summary>
public sealed record Address
{
    public required string Street { get; init; }

    public required string PostalCode { get; init; }

    public required string City { get; init; }

    public required string Country { get; init; }
}
