using System.Net;
using System.Net.Http.Json;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class ConcurrencyApiTests(ApiFixture fixture)
{
    private HttpClient Client => fixture.Manager;

    [Fact]
    public async Task Reading_a_warehouse_publishes_its_version()
    {
        var warehouse = await CreateWarehouseAsync();

        var response = await Client.GetAsync($"/warehouse/{warehouse.Id}");
        var body = await response.Content.ReadFromJsonAsync<WarehouseResponse>();

        Assert.NotNull(response.Headers.ETag);
        Assert.True(response.Headers.ETag!.IsWeak == false);
        // The header and the body carry the same token, base64 encoded.
        Assert.Equal($"\"{Convert.ToBase64String(body!.RowVersion)}\"", response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task Creating_a_warehouse_publishes_a_version_straight_away()
    {
        var response = await Client.PostAsJsonAsync("/warehouse", NewWarehouse());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
    }

    [Fact]
    public async Task A_write_without_a_precondition_is_refused()
    {
        var warehouse = await CreateWarehouseAsync();

        var response = await Client.PutAsJsonAsync($"/warehouse/{warehouse.Id}", Update());

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("If-Match required", problem!.Title);
    }

    [Fact]
    public async Task A_wildcard_precondition_is_refused_because_it_would_overwrite_anything()
    {
        var warehouse = await CreateWarehouseAsync();

        var response = await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}", Update(), "*");

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
    }

    [Fact]
    public async Task A_precondition_that_is_not_an_etag_is_a_bad_request()
    {
        var warehouse = await CreateWarehouseAsync();

        var response = await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}", Update(), "\"not base64!\"");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_write_with_the_current_version_succeeds_and_moves_the_version_on()
    {
        var warehouse = await CreateWarehouseAsync();
        var before = await Client.GetETagAsync($"/warehouse/{warehouse.Id}");

        var response = await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}", Update("Renamed once"), before);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.NotEqual(before, response.Headers.ETag!.ToString());

        var after = await Client.GetETagAsync($"/warehouse/{warehouse.Id}");
        Assert.NotEqual(before, after);
        Assert.Equal(response.Headers.ETag.ToString(), after);
    }

    [Fact]
    public async Task The_second_of_two_writers_holding_the_same_version_loses()
    {
        var warehouse = await CreateWarehouseAsync();

        // Both clients read the same version, as two browser tabs would.
        var sharedVersion = await Client.GetETagAsync($"/warehouse/{warehouse.Id}");

        var first = await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}", Update("First writer wins"), sharedVersion);
        var second = await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}", Update("Second writer overwrites"), sharedVersion);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("Version mismatch", problem!.Title);

        // The first writer's change is intact: no silent overwrite.
        var current = await Client.GetFromJsonAsync<WarehouseResponse>($"/warehouse/{warehouse.Id}");
        Assert.Equal("First writer wins", current!.Name);
    }

    [Fact]
    public async Task Retrying_with_the_fresh_version_then_succeeds()
    {
        var warehouse = await CreateWarehouseAsync();
        var stale = await Client.GetETagAsync($"/warehouse/{warehouse.Id}");

        await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}", Update("Someone else"), stale);

        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}", Update("Mine"), stale)).StatusCode);

        // Read again, retry, and it lands.
        var fresh = await Client.GetETagAsync($"/warehouse/{warehouse.Id}");
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}", Update("Mine"), fresh)).StatusCode);

        var current = await Client.GetFromJsonAsync<WarehouseResponse>($"/warehouse/{warehouse.Id}");
        Assert.Equal("Mine", current!.Name);
    }

    [Fact]
    public async Task Deleting_with_a_stale_version_is_refused_and_the_warehouse_survives()
    {
        var warehouse = await CreateWarehouseAsync();
        var stale = await Client.GetETagAsync($"/warehouse/{warehouse.Id}");

        await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}", Update("Changed under the deleter"), stale);

        var deleted = await Client.DeleteIfMatchAsync($"/warehouse/{warehouse.Id}", stale);

        Assert.Equal(HttpStatusCode.PreconditionFailed, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/warehouse/{warehouse.Id}")).StatusCode);
    }

    [Fact]
    public async Task Stock_lines_carry_their_own_version_not_the_warehouse_one()
    {
        var warehouse = await CreateWarehouseAsync();
        var item = await AddItemAsync(warehouse.Id, "VER-1");

        var warehouseVersion = await Client.GetETagAsync($"/warehouse/{warehouse.Id}");
        var itemVersion = await Client.GetETagAsync($"/warehouse/{warehouse.Id}/items/{item.Id}");

        Assert.NotEqual(warehouseVersion, itemVersion);

        // The warehouse's version is not a licence to write the item.
        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}/items/{item.Id}",
                new { name = "Wrong token", quantity = 1 }, warehouseVersion)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}/items/{item.Id}",
                new { name = "Right token", quantity = 1 }, itemVersion)).StatusCode);
    }

    [Fact]
    public async Task The_second_of_two_writers_on_a_stock_line_loses()
    {
        var warehouse = await CreateWarehouseAsync();
        var item = await AddItemAsync(warehouse.Id, "VER-2");

        var shared = await Client.GetETagAsync($"/warehouse/{warehouse.Id}/items/{item.Id}");

        var first = await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}/items/{item.Id}",
            new { name = "First writer", quantity = 10 }, shared);
        var second = await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}/items/{item.Id}",
            new { name = "Second writer", quantity = 0 }, shared);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);

        var current = await Client.GetFromJsonAsync<ItemResponse>($"/warehouse/{warehouse.Id}/items/{item.Id}");
        Assert.Equal("First writer", current!.Name);
        Assert.Equal(10, current.Quantity);
        Assert.True(current.IsOnStock);
    }

    [Fact]
    public async Task Deleting_a_stock_line_with_a_stale_version_is_refused()
    {
        var warehouse = await CreateWarehouseAsync();
        var item = await AddItemAsync(warehouse.Id, "VER-3");

        var stale = await Client.GetETagAsync($"/warehouse/{warehouse.Id}/items/{item.Id}");
        await Client.PutIfMatchAsync($"/warehouse/{warehouse.Id}/items/{item.Id}",
            new { name = "Changed", quantity = 2 }, stale);

        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await Client.DeleteIfMatchAsync($"/warehouse/{warehouse.Id}/items/{item.Id}", stale)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Client.GetAsync($"/warehouse/{warehouse.Id}/items/{item.Id}")).StatusCode);
    }

    private async Task<WarehouseResponse> CreateWarehouseAsync()
    {
        var response = await Client.PostAsJsonAsync("/warehouse", NewWarehouse());
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<WarehouseResponse>())!;
    }

    private async Task<ItemResponse> AddItemAsync(Guid warehouseId, string sku)
    {
        var response = await Client.PostAsJsonAsync($"/warehouse/{warehouseId}/items",
            new { sku, name = $"Stock {sku}", quantity = 5 });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ItemResponse>())!;
    }

    private static object NewWarehouse()
    {
        var code = ApiFixture.UniqueCode();

        return new
        {
            code,
            name = $"Depot {code}",
            address = new { street = "Havnegata 4", postalCode = "4005", city = "Stavanger", country = "Norway" },
            capacityInPallets = 500
        };
    }

    private static object Update(string name = "Updated depot") => new
    {
        name,
        address = new { street = "Havnegata 8", postalCode = "4005", city = "Stavanger", country = "Norway" },
        capacityInPallets = 900,
        isActive = true
    };
}
