using Microsoft.EntityFrameworkCore;
using SmartCraftTask.Models;

namespace SmartCraftTask.Data;

public static class DatabaseInitializer
{
    /// <summary>
    /// Brings the schema up to date and, on an empty database, plants the sample aggregates.
    /// Fine for this task; a production system would apply migrations as a deployment step instead.
    /// </summary>
    public static async Task MigrateAndSeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.MigrateAsync(cancellationToken);

        if (await context.Warehouses.AnyAsync(cancellationToken))
        {
            return;
        }

        // Fixed ids and timestamps so the seeded rows stay predictable across restarts,
        // which is what the .http scratch file and the tests lean on.
        var seededAt = new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero);

        var oslo = Warehouse.Register(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "OSL-01",
            "Oslo Central",
            new Address { Street = "Karl Johans gate 1", PostalCode = "0154", City = "Oslo", Country = "Norway" },
            capacityInPallets: 1200,
            seededAt);

        oslo.AddItem("PAL-1001", "Oak plank 2m", quantity: 12);
        oslo.AddItem("PAL-2002", "Pine beam 4m", quantity: 0);

        var bergen = Warehouse.Register(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "BGO-01",
            "Bergen Harbour",
            new Address { Street = "Bryggen 12", PostalCode = "5003", City = "Bergen", Country = "Norway" },
            capacityInPallets: 800,
            seededAt);

        bergen.AddItem("PAL-1001", "Oak plank 2m", quantity: 5);

        // Deactivated, so it also demonstrates the rule that stock cannot be added to it.
        var trondheim = Warehouse.Register(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "TRD-01",
            "Trondheim Overflow",
            new Address { Street = "Kongens gate 30", PostalCode = "7012", City = "Trondheim", Country = "Norway" },
            capacityInPallets: 350,
            seededAt,
            isActive: false);

        context.Warehouses.AddRange(oslo, bergen, trondheim);

        await context.SaveChangesAsync(cancellationToken);
    }
}
