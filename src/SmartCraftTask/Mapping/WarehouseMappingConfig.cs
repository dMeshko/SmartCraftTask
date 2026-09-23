using Mapster;
using SmartCraftTask.Dtos;
using SmartCraftTask.Models;

namespace SmartCraftTask.Mapping;

/// <summary>
/// Outbound only. Nothing maps *into* an aggregate: a Warehouse is built by Warehouse.Register and
/// changed through its own methods, so its invariants cannot be bypassed by a mapper. Address is the
/// exception, and deliberately so: a value object has no invariants beyond its shape.
/// </summary>
public sealed class WarehouseMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Address, AddressDto>();
        config.NewConfig<AddressDto, Address>();

        config.NewConfig<Warehouse, WarehouseResponse>()
            // Count() rather than .Count: the extension method is what EF turns into a
            // COUNT(*) subquery. The property form makes it load every item and count in memory.
            .Map(destination => destination.ItemCount, source => source.Items.Count());
    }
}
