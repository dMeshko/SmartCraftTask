using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCraftTask.Data;
using SmartCraftTask.Dtos;
using SmartCraftTask.Models;

namespace SmartCraftTask.Controllers;

[ApiController]
[Route("warehouse")]
public class WarehouseController(ApplicationDbContext context, IMapper mapper) : ControllerBase
{
    /// <summary>Lists warehouses, optionally filtered by their active flag.</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<WarehouseResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<WarehouseResponse>>> GetAll(
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        var warehouses = await context.Warehouses
            .AsNoTracking()
            .Where(warehouse => isActive == null || warehouse.IsActive == isActive)
            .OrderBy(warehouse => warehouse.Code)
            .ProjectToType<WarehouseResponse>(mapper.Config)
            .ToListAsync(cancellationToken);

        return Ok(warehouses);
    }

    /// <summary>Fetches a single warehouse.</summary>
    [HttpGet("{id:guid}", Name = nameof(GetById))]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WarehouseResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        // Projected rather than loaded so ItemCount is counted in SQL, exactly as in GetAll.
        var warehouse = await context.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.Id == id)
            .ProjectToType<WarehouseResponse>(mapper.Config)
            .FirstOrDefaultAsync(cancellationToken);

        return warehouse is null ? NotFound() : Ok(warehouse);
    }

    /// <summary>Registers a new warehouse.</summary>
    [HttpPost]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WarehouseResponse>> Create(
        CreateWarehouseRequest request,
        CancellationToken cancellationToken)
    {
        // A conflict rather than a validation error: the payload is well formed, the world disagrees.
        if (await context.Warehouses.AnyAsync(warehouse => warehouse.Code == request.Code, cancellationToken))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Duplicate warehouse code",
                detail: $"A warehouse with code '{request.Code}' already exists.");
        }

        var warehouse = Warehouse.Register(
            request.Code,
            request.Name,
            mapper.Map<Address>(request.Address!),
            request.CapacityInPallets);

        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync(cancellationToken);

        return CreatedAtRoute(
            nameof(GetById),
            new { id = warehouse.Id },
            mapper.Map<WarehouseResponse>(warehouse));
    }

    /// <summary>Replaces the mutable details of an existing warehouse.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateWarehouseRequest request,
        CancellationToken cancellationToken)
    {
        var warehouse = await context.Warehouses
            .FirstOrDefaultAsync(warehouse => warehouse.Id == id, cancellationToken);

        if (warehouse is null)
        {
            return NotFound();
        }

        // Spelled out through the aggregate's own vocabulary rather than blind-copied from the DTO.
        warehouse.Rename(request.Name);
        warehouse.Relocate(mapper.Map<Address>(request.Address!));
        warehouse.Resize(request.CapacityInPallets);

        if (request.IsActive)
        {
            warehouse.Activate();
        }
        else
        {
            warehouse.Deactivate();
        }

        await context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Removes a warehouse.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await context.Warehouses
            .Where(warehouse => warehouse.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? NotFound() : NoContent();
    }
}
