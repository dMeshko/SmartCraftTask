using Microsoft.EntityFrameworkCore;
using SmartCraftTask.Models;

namespace SmartCraftTask.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Picks up every IEntityTypeConfiguration in this assembly, so new entities only need their
        // own configuration class rather than an edit here.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
