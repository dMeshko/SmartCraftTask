using System.Net;
using System.Net.Http.Json;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Tests;

[Collection(nameof(ApiCollection))]
public sealed class WarehouseApiTests(ApiFixture fixture)
{
    // These cover behaviour rather than authorisation, so they run as a manager.
    private HttpClient Client => fixture.Manager;

    [Fact]
    public async Task Seeded_warehouses_are_listed_with_their_item_counts()
    {
        var warehouses = await Client.GetFromJsonAsync<List<WarehouseResponse>>("/warehouse");

        Assert.NotNull(warehouses);
        var oslo = Assert.Single(warehouses, warehouse => warehouse.Code == "OSL-01");
        Assert.Equal(2, oslo.ItemCount);
        Assert.Equal("Oslo", oslo.Address.City);
    }

    [Fact]
    public async Task The_active_filter_selects_only_deactivated_warehouses()
    {
        var warehouses = await Client.GetFromJsonAsync<List<WarehouseResponse>>("/warehouse?isActive=false");

        Assert.NotNull(warehouses);
        Assert.All(warehouses, warehouse => Assert.False(warehouse.IsActive));
        Assert.Contains(warehouses, warehouse => warehouse.Code == "TRD-01");
    }

    [Fact]
    public async Task An_unknown_warehouse_is_not_found()
    {
        var response = await Client.GetAsync($"/warehouse/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_warehouse_returns_201_and_a_location_that_resolves()
    {
        var code = ApiFixture.UniqueCode();

        var response = await Client.PostAsJsonAsync("/warehouse", Create(code));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await response.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.NotNull(created);
        Assert.Equal(code, created.Code);
        Assert.True(created.IsActive);
        Assert.Null(created.UpdatedAt);
        Assert.Equal(0, created.ItemCount);

        var fetched = await Client.GetFromJsonAsync<WarehouseResponse>(response.Headers.Location);
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task A_duplicate_code_is_a_conflict_not_a_validation_error()
    {
        var code = ApiFixture.UniqueCode();
        await Client.PostAsJsonAsync("/warehouse", Create(code));

        var response = await Client.PostAsJsonAsync("/warehouse", Create(code));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("Duplicate warehouse code", problem!.Title);
    }

    [Fact]
    public async Task Validation_failures_come_back_as_problem_details_per_field()
    {
        var response = await Client.PostAsJsonAsync("/warehouse", new
        {
            code = "bad code!",
            name = "",
            capacityInPallets = -5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.NotNull(problem?.Errors);
        Assert.Contains("Code", problem.Errors.Keys);
        Assert.Contains("Name", problem.Errors.Keys);
        Assert.Contains("Address", problem.Errors.Keys);
        Assert.Contains("CapacityInPallets", problem.Errors.Keys);
    }

    [Fact]
    public async Task Nested_address_failures_are_reported_against_the_nested_path()
    {
        var response = await Client.PostAsJsonAsync("/warehouse", new
        {
            code = ApiFixture.UniqueCode(),
            name = "Valid name",
            address = new { street = "a", postalCode = "", city = "", country = "Norway" },
            capacityInPallets = 10
        });

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Address.Street", problem!.Errors!.Keys);
        Assert.Contains("Address.City", problem.Errors.Keys);
    }

    [Fact]
    public async Task Updating_changes_the_mutable_details_and_leaves_the_code_fixed()
    {
        var code = ApiFixture.UniqueCode();
        var created = await CreateWarehouseAsync(code);

        var response = await Client.PutAsJsonAsync($"/warehouse/{created.Id}", new
        {
            name = "Renamed depot",
            address = new { street = "Havnegata 8", postalCode = "4005", city = "Stavanger", country = "Norway" },
            capacityInPallets = 900,
            isActive = false,
            // Neither is part of the update contract; both must be ignored.
            code = "HACK-99",
            createdAt = "1999-01-01T00:00:00+00:00"
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<WarehouseResponse>($"/warehouse/{created.Id}");
        Assert.Equal("Renamed depot", updated!.Name);
        Assert.Equal(900, updated.CapacityInPallets);
        Assert.False(updated.IsActive);
        Assert.Equal("Havnegata 8", updated.Address.Street);
        Assert.Equal(code, updated.Code);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Updating_an_unknown_warehouse_is_not_found()
    {
        var response = await Client.PutAsJsonAsync($"/warehouse/{Guid.NewGuid()}", new
        {
            name = "Ghost",
            address = new { street = "Nowhere 1", postalCode = "0000", city = "X", country = "Y" },
            capacityInPallets = 1,
            isActive = true
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_warehouse_takes_its_stock_lines_with_it()
    {
        var created = await CreateWarehouseAsync(ApiFixture.UniqueCode());
        await Client.PostAsJsonAsync($"/warehouse/{created.Id}/items",
            new { sku = "CASCADE-1", name = "Doomed stock", quantity = 4 });

        var itemsBefore = await Client.GetFromJsonAsync<List<ItemResponse>>($"/warehouse/{created.Id}/items");
        Assert.Single(itemsBefore!);

        var deleted = await Client.DeleteAsync($"/warehouse/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/warehouse/{created.Id}")).StatusCode);
        // The items are addressed through the parent, so their route 404s once it is gone.
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/warehouse/{created.Id}/items")).StatusCode);
    }

    [Fact]
    public async Task Deleting_twice_is_not_found_the_second_time()
    {
        var created = await CreateWarehouseAsync(ApiFixture.UniqueCode());

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/warehouse/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/warehouse/{created.Id}")).StatusCode);
    }

    private async Task<WarehouseResponse> CreateWarehouseAsync(string code)
    {
        var response = await Client.PostAsJsonAsync("/warehouse", Create(code));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<WarehouseResponse>())!;
    }

    private static object Create(string code) => new
    {
        code,
        name = $"Depot {code}",
        address = new { street = "Havnegata 4", postalCode = "4005", city = "Stavanger", country = "Norway" },
        capacityInPallets = 500
    };
}

/// <summary>Just enough of RFC 9457 to assert on.</summary>
public sealed record ProblemShape
{
    public string? Title { get; init; }

    public int Status { get; init; }

    public string? Detail { get; init; }

    public Dictionary<string, string[]>? Errors { get; init; }
}
