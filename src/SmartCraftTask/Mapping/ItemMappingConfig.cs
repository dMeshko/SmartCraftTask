using Mapster;
using SmartCraftTask.Dtos;
using SmartCraftTask.Models;

namespace SmartCraftTask.Mapping;

public sealed class ItemMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Item, ItemResponse>();
    }
}
