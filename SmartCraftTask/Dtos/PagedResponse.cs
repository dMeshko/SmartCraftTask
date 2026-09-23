namespace SmartCraftTask.Dtos;

/// <summary>
/// One page of results plus what a client needs to walk the rest. The envelope goes in the body
/// rather than into response headers so the shape is part of the OpenAPI schema, visible in
/// Swagger UI without a transformer, and readable from a browser without CORS having to expose
/// each header by name.
/// </summary>
public sealed record PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    public required PageMetadata Page { get; init; }
}

/// <summary>
/// Describes the page that was served. <see cref="TotalPageCount"/> and the two flags are derived
/// here rather than assigned, so they cannot contradict the numbers they are derived from.
/// </summary>
public sealed record PageMetadata
{
    public PageMetadata(int totalItemCount, int pageNumber, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        TotalItemCount = totalItemCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalPageCount = (int)Math.Ceiling(totalItemCount / (double)pageSize);
    }

    /// <summary>Rows matching the filter, across every page — not the count in <c>Items</c>.</summary>
    public int TotalItemCount { get; }

    public int TotalPageCount { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public bool HasPrevious => PageNumber > 1;

    public bool HasNext => PageNumber < TotalPageCount;
}
