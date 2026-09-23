using Microsoft.AspNetCore.Authorization;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCraftTask.Auth;
using SmartCraftTask.Data;
using SmartCraftTask.Dtos;
using SmartCraftTask.Infrastructure;
using SmartCraftTask.Models;

namespace SmartCraftTask.Controllers;

[ApiController]
[Route("warehouse")]
public class WarehouseController(ApplicationDbContext context, IMapper mapper) : ConditionalControllerBase
{
    /// <summary>Lists warehouses a page at a time, optionally filtered by their active flag.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.ReadWarehouses)]
    [ProducesResponseType<PagedResponse<WarehouseResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<WarehouseResponse>>> GetAll(
        [FromQuery] bool? isActive,
        [FromQuery] PageRequest page,
        CancellationToken cancellationToken)
    {
        // Ordered by Code before paging: unique and stable, so a row cannot shift between pages.
        var warehouses = await context.Warehouses
            .AsNoTracking()
            .Where(warehouse => isActive == null || warehouse.IsActive == isActive)
            .OrderBy(warehouse => warehouse.Code)
            .ProjectToType<WarehouseResponse>(mapper.Config)
            .ToPagedResponseAsync(page, cancellationToken);

        return Ok(warehouses);
    }

    /// <summary>Fetches a single warehouse.</summary>
    [HttpGet("{id:guid}", Name = nameof(GetById))]
    [Authorize(Policy = Policies.ReadWarehouses)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WarehouseResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        // Projected rather than loaded so ItemCount is counted in SQL, exactly as in GetAll.
        var warehouse = await context.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.Id == id)
            .ProjectToType<WarehouseResponse>(mapper.Config)
            .FirstOrDefaultAsync(cancellationToken);

        if (warehouse is null)
        {
            return NotFound();
        }

        SetETag(warehouse.RowVersion);

        return Ok(warehouse);
    }

    /// <summary>Registers a new warehouse.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.ManageWarehouses)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

        SetETag(warehouse.RowVersion);

        return CreatedAtRoute(
            nameof(GetById),
            new { id = warehouse.Id },
            mapper.Map<WarehouseResponse>(warehouse));
    }

    /// <summary>Replaces the mutable details of an existing warehouse.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageWarehouses)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateWarehouseRequest request,
        CancellationToken cancellationToken)
    {
        // The precondition is part of the request, so it is checked before anything is looked up.
        if (!TryGetExpectedVersion(out var expectedVersion, out var failure))
        {
            return failure!;
        }

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

        // Telling EF which version we read puts it in the UPDATE's WHERE clause, so the database
        // decides the race rather than a comparison that could go stale in between.
        context.Entry(warehouse).Property(entity => entity.RowVersion).OriginalValue = expectedVersion;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PreconditionFailed(
                $"Warehouse '{id}' changed since the version you read. Fetch it again and retry.");
        }

        SetETag(warehouse.RowVersion);

        return NoContent();
    }

    /// <summary>Removes a warehouse.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ManageWarehouses)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetExpectedVersion(out var expectedVersion, out var failure))
        {
            return failure!;
        }

        // Loaded rather than deleted in one statement: ExecuteDelete bypasses the change tracker,
        // and with it the concurrency token. Children still go by the database's cascade.
        var warehouse = await context.Warehouses
            .FirstOrDefaultAsync(warehouse => warehouse.Id == id, cancellationToken);

        if (warehouse is null)
        {
            return NotFound();
        }

        context.Entry(warehouse).Property(entity => entity.RowVersion).OriginalValue = expectedVersion;
        context.Warehouses.Remove(warehouse);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PreconditionFailed(
                $"Warehouse '{id}' changed since the version you read. Fetch it again and retry.");
        }

        return NoContent();
    }
}
