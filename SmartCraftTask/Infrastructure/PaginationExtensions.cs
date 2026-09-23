using Microsoft.EntityFrameworkCore;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Infrastructure;

public static class PaginationExtensions
{
    /// <summary>
    /// Counts the rows the query matches, then fetches only the requested window of them.
    /// </summary>
    /// <remarks>
    /// Two round trips on purpose: the count has to span every page, so it cannot come from the
    /// page itself. The caller must have ordered the query — paging an unordered query lets the
    /// database return rows in any order it likes, which can repeat or skip rows between pages.
    /// </remarks>
    public static async Task<PagedResponse<T>> ToPagedResponseAsync<T>(
        this IQueryable<T> source,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var totalItemCount = await source.CountAsync(cancellationToken);

        // A page past the end is an empty page, not a 404: "nothing here" is a legitimate answer
        // to a well-formed question, and HasNext/HasPrevious still tell the client where it is.
        var items = await source
            .Skip((page.PageNumber - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<T>
        {
            Items = items,
            Page = new PageMetadata(totalItemCount, page.PageNumber, page.PageSize)
        };
    }
}
