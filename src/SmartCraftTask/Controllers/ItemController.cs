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

/// <summary>
/// Stock lines, addressed through the warehouse that holds them. Every action confirms the
/// parent exists first, so a bad warehouse id is a 404 rather than an empty list or a stray row.
/// </summary>
[ApiController]
[Route("warehouse/{warehouseId:guid}/items")]
public class ItemController(ApplicationDbContext context, IMapper mapper) : ConditionalControllerBase
{
    /// <summary>Lists the stock lines in a warehouse a page at a time, optionally filtered by availability.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.ReadStock)]
    [ProducesResponseType<PagedResponse<ItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<ItemResponse>>> GetAll(
        Guid warehouseId,
        [FromQuery] bool? isOnStock,
        [FromQuery] PageRequest page,
        CancellationToken cancellationToken)
    {
        if (!await WarehouseExistsAsync(warehouseId, cancellationToken))
        {
            return WarehouseNotFound(warehouseId);
        }

        // Ordered by Sku before paging: unique within a warehouse, so paging is stable.
        var items = await ItemsIn(warehouseId)
            .Where(item => isOnStock == null || item.IsOnStock == isOnStock)
            .OrderBy(item => item.Sku)
            .ProjectToType<ItemResponse>(mapper.Config)
            .ToPagedResponseAsync(page, cancellationToken);

        return Ok(items);
    }

    /// <summary>Fetches a single stock line.</summary>
    [HttpGet("{id:guid}", Name = nameof(GetItemById))]
    [Authorize(Policy = Policies.ReadStock)]
    [ProducesResponseType<ItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItemResponse>> GetItemById(
        Guid warehouseId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await ItemsIn(warehouseId)
            .Where(candidate => candidate.Id == id)
            .ProjectToType<ItemResponse>(mapper.Config)
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        SetETag(item.RowVersion);

        // Set the tag first: a 304 has to carry the same ETag a 200 would have.
        if (CallerAlreadyHasCurrent(item.RowVersion))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(item);
    }

    /// <summary>Adds a stock line to a warehouse.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.ManageStock)]
    [ProducesResponseType<ItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ItemResponse>> Create(
        Guid warehouseId,
        CreateItemRequest request,
        CancellationToken cancellationToken)
    {
        var warehouse = await LoadAggregateAsync(warehouseId, cancellationToken);

        if (warehouse is null)
        {
            return WarehouseNotFound(warehouseId);
        }

        // Duplicate SKUs and deactivated warehouses are the aggregate's business, not the
        // controller's: AddItem throws a DomainException, which surfaces as 409.
        var item = warehouse.AddItem(request.Sku, request.Name, request.Quantity);

        await context.SaveChangesAsync(cancellationToken);

        SetETag(item.RowVersion);

        return CreatedAtRoute(
            nameof(GetItemById),
            new { warehouseId, id = item.Id },
            mapper.Map<ItemResponse>(item));
    }

    /// <summary>Replaces the mutable details of a stock line.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageStock)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(
        Guid warehouseId,
        Guid id,
        UpdateItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetExpectedVersion(out var expectedVersion, out var failure))
        {
            return failure!;
        }

        var warehouse = await LoadAggregateAsync(warehouseId, cancellationToken);

        if (warehouse is null)
        {
            return WarehouseNotFound(warehouseId);
        }

        // The version guarded here is the stock line's own, so the token has to be reached before
        // the change is applied.
        var item = warehouse.FindItem(id);

        if (item is null)
        {
            return NotFound();
        }

        context.Entry(item).Property(entity => entity.RowVersion).OriginalValue = expectedVersion;

        warehouse.TryUpdateItem(id, request.Name, request.Quantity);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PreconditionFailed(
                $"Stock line '{id}' changed since the version you read. Fetch it again and retry.");
        }

        SetETag(item.RowVersion);

        return NoContent();
    }

    /// <summary>Removes a stock line.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ManageStock)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Delete(Guid warehouseId, Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetExpectedVersion(out var expectedVersion, out var failure))
        {
            return failure!;
        }

        var warehouse = await LoadAggregateAsync(warehouseId, cancellationToken);

        if (warehouse is null)
        {
            return WarehouseNotFound(warehouseId);
        }

        var item = warehouse.FindItem(id);

        if (item is null)
        {
            return NotFound();
        }

        context.Entry(item).Property(entity => entity.RowVersion).OriginalValue = expectedVersion;

        warehouse.TryRemoveItem(id);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PreconditionFailed(
                $"Stock line '{id}' changed since the version you read. Fetch it again and retry.");
        }

        return NoContent();
    }

    /// <summary>
    /// Reads bypass the aggregate on purpose: projecting straight to a DTO keeps the work in SQL.
    /// </summary>
    private IQueryable<Item> ItemsIn(Guid warehouseId) =>
        context.Items.AsNoTracking().Where(item => item.WarehouseId == warehouseId);

    private Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        context.Warehouses.AnyAsync(warehouse => warehouse.Id == warehouseId, cancellationToken);

    /// <summary>
    /// Writes load the whole aggregate, items included, because that is what the root needs to
    /// enforce its invariants. The cost is real and grows with the number of stock lines.
    /// </summary>
    private Task<Warehouse?> LoadAggregateAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        context.Warehouses
            .Include(warehouse => warehouse.Items)
            .FirstOrDefaultAsync(warehouse => warehouse.Id == warehouseId, cancellationToken);

    private ObjectResult WarehouseNotFound(Guid warehouseId) =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Warehouse not found",
            detail: $"No warehouse with id '{warehouseId}' exists.");
}
