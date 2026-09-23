using Microsoft.AspNetCore.Mvc;

namespace SmartCraftTask.Dtos;

/// <summary>
/// The paging window a client asks for. Bound from the query string on every list endpoint, so
/// both lists are paged the same way and neither can grow an unbounded response.
/// </summary>
// Validation rules live in PageRequestValidator.
public sealed record PageRequest
{
    /// <summary>Small enough to page visibly in the UI, large enough not to annoy a real client.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>A ceiling, not a suggestion: over this the request is refused rather than trimmed.</summary>
    public const int MaxPageSize = 100;

    // Named explicitly: without this the API explorer advertises the property names, PascalCase,
    // while every other query parameter in the document is camelCase. Binding accepts either.
    /// <summary>One-based: the first page is 1. Page 0 is a client bug, not page 1.</summary>
    [FromQuery(Name = "pageNumber")]
    public int PageNumber { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    public int PageSize { get; init; } = DefaultPageSize;
}
