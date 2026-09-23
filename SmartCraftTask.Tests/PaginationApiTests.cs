using System.Net;
using System.Net.Http.Json;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Tests;

/// <summary>
/// Paging on both list endpoints. The warehouse tests deliberately assert nothing about the exact
/// total: other tests in this collection create warehouses, so the totals move. What must hold is
/// that the pages agree with each other and with the unpaged view taken in the same test.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class PaginationApiTests(ApiFixture fixture)
{
    private HttpClient Client => fixture.Manager;

    [Fact]
    public async Task A_list_is_paged_by_default_and_reports_the_total_across_every_page()
    {
        var page = await WarehousePageAsync("/warehouse");

        Assert.Equal(1, page.Page.PageNumber);
        Assert.Equal(PageRequest.DefaultPageSize, page.Page.PageSize);
        Assert.False(page.Page.HasPrevious);

        // The three seeded warehouses are always present, whatever else the collection has added.
        Assert.True(page.Page.TotalItemCount >= 3);

        // The count spans every page, so it is not the length of this one.
        Assert.Equal(Math.Min(PageRequest.DefaultPageSize, page.Page.TotalItemCount), page.Items.Count);
    }

    [Fact]
    public async Task Walking_the_pages_returns_every_row_exactly_once_and_in_order()
    {
        var unpaged = await WarehousePageAsync($"/warehouse?pageSize={PageRequest.MaxPageSize}");
        Assert.True(
            unpaged.Page.TotalItemCount <= PageRequest.MaxPageSize,
            "this test assumes a single maximum-size page can hold every warehouse");

        var walked = new List<string>();

        for (var pageNumber = 1; pageNumber <= unpaged.Page.TotalItemCount + 1; pageNumber++)
        {
            var page = await WarehousePageAsync($"/warehouse?pageNumber={pageNumber}&pageSize=2");

            Assert.Equal(unpaged.Page.TotalItemCount, page.Page.TotalItemCount);
            walked.AddRange(page.Items.Select(warehouse => warehouse.Code));

            if (!page.Page.HasNext)
            {
                break;
            }
        }

        // Same rows, same order, nothing repeated across a page boundary and nothing skipped.
        Assert.Equal(unpaged.Items.Select(warehouse => warehouse.Code).ToArray(), walked.ToArray());
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_rather_than_missing()
    {
        var first = await WarehousePageAsync("/warehouse?pageSize=2");
        var past = await WarehousePageAsync($"/warehouse?pageNumber={first.Page.TotalPageCount + 5}&pageSize=2");

        Assert.Empty(past.Items);
        Assert.Equal(first.Page.TotalItemCount, past.Page.TotalItemCount);
        Assert.False(past.Page.HasNext);
        Assert.True(past.Page.HasPrevious);
    }

    [Theory]
    [InlineData("pageNumber=0")]
    [InlineData("pageNumber=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task A_window_outside_the_allowed_range_is_refused(string query)
    {
        var response = await Client.GetAsync($"/warehouse?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Refused, not quietly clamped, and the problem names the parameter at fault.
        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        var offending = query.Split('=')[0];
        Assert.NotNull(problem?.Errors);
        Assert.Contains(problem.Errors.Keys, key => key.Equals(offending, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Stock_lines_are_paged_within_their_own_warehouse()
    {
        var warehouse = await CreateWarehouseAsync();

        foreach (var sku in new[] { "PAGE-1", "PAGE-2", "PAGE-3", "PAGE-4", "PAGE-5" })
        {
            await AddItemAsync(warehouse.Id, sku, quantity: 3);
        }

        var first = await ItemPageAsync($"/warehouse/{warehouse.Id}/items?pageNumber=1&pageSize=2");
        var second = await ItemPageAsync($"/warehouse/{warehouse.Id}/items?pageNumber=2&pageSize=2");
        var third = await ItemPageAsync($"/warehouse/{warehouse.Id}/items?pageNumber=3&pageSize=2");

        Assert.Equal(5, first.Page.TotalItemCount);
        Assert.Equal(3, first.Page.TotalPageCount);

        Assert.Equal(new[] { "PAGE-1", "PAGE-2" }, first.Items.Select(item => item.Sku).ToArray());
        Assert.Equal(new[] { "PAGE-3", "PAGE-4" }, second.Items.Select(item => item.Sku).ToArray());
        Assert.Equal(new[] { "PAGE-5" }, third.Items.Select(item => item.Sku).ToArray());

        Assert.True(second.Page.HasPrevious);
        Assert.True(second.Page.HasNext);
        Assert.False(third.Page.HasNext);
    }

    [Fact]
    public async Task The_total_counts_what_the_filter_matched_not_the_whole_table()
    {
        var warehouse = await CreateWarehouseAsync();
        await AddItemAsync(warehouse.Id, "FILTER-1", quantity: 5);
        await AddItemAsync(warehouse.Id, "FILTER-2", quantity: 5);
        await AddItemAsync(warehouse.Id, "FILTER-3", quantity: 0);

        var onStock = await ItemPageAsync($"/warehouse/{warehouse.Id}/items?isOnStock=true&pageSize=1");

        // Three lines in the warehouse, two of them on stock: the filter runs before the count.
        Assert.Equal(2, onStock.Page.TotalItemCount);
        Assert.Equal(2, onStock.Page.TotalPageCount);
        Assert.Single(onStock.Items);
        Assert.True(onStock.Page.HasNext);
    }

    private async Task<PagedResponse<WarehouseResponse>> WarehousePageAsync(string url) =>
        (await Client.GetFromJsonAsync<PagedResponse<WarehouseResponse>>(url))!;

    private async Task<PagedResponse<ItemResponse>> ItemPageAsync(string url) =>
        (await Client.GetFromJsonAsync<PagedResponse<ItemResponse>>(url))!;

    private async Task<WarehouseResponse> CreateWarehouseAsync()
    {
        var code = ApiFixture.UniqueCode();

        var response = await Client.PostAsJsonAsync("/warehouse", new
        {
            code,
            name = $"Depot {code}",
            address = new { street = "Havnegata 4", postalCode = "4005", city = "Stavanger", country = "Norway" },
            capacityInPallets = 500
        });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<WarehouseResponse>())!;
    }

    private async Task<ItemResponse> AddItemAsync(Guid warehouseId, string sku, int quantity)
    {
        var response = await Client.PostAsJsonAsync($"/warehouse/{warehouseId}/items",
            new { sku, name = $"Stock {sku}", quantity });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ItemResponse>())!;
    }
}
