using System.Net;
using System.Net.Http.Json;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Tests;

[Collection(nameof(ApiCollection))]
public sealed class ItemApiTests(ApiFixture fixture)
{
    private static readonly Guid Oslo = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bergen = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DeactivatedTrondheim = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // These cover behaviour rather than authorisation, so they run as a manager.
    private HttpClient Client => fixture.Manager;

    [Fact]
    public async Task Seeded_stock_lines_are_listed_for_their_warehouse()
    {
        var items = await Client.GetFromJsonAsync<List<ItemResponse>>($"/warehouse/{Oslo}/items");

        Assert.NotNull(items);
        Assert.Contains(items, item => item.Sku == "PAL-1001" && item.Quantity == 12 && item.IsOnStock);
        Assert.Contains(items, item => item.Sku == "PAL-2002" && item.Quantity == 0 && !item.IsOnStock);
        Assert.All(items, item => Assert.Equal(Oslo, item.WarehouseId));
    }

    [Fact]
    public async Task The_on_stock_filter_is_answered_from_the_computed_column()
    {
        var onStock = await Client.GetFromJsonAsync<List<ItemResponse>>($"/warehouse/{Oslo}/items?isOnStock=true");
        var offStock = await Client.GetFromJsonAsync<List<ItemResponse>>($"/warehouse/{Oslo}/items?isOnStock=false");

        Assert.All(onStock!, item => Assert.True(item.Quantity > 0));
        Assert.All(offStock!, item => Assert.Equal(0, item.Quantity));
        Assert.Contains(offStock!, item => item.Sku == "PAL-2002");
    }

    [Fact]
    public async Task IsOnStock_follows_quantity_without_anything_setting_it()
    {
        var warehouse = await CreateWarehouseAsync();
        var item = await AddItemAsync(warehouse.Id, "STOCK-1", quantity: 7);

        Assert.True(item.IsOnStock);

        var response = await Client.PutCurrentAsync($"/warehouse/{warehouse.Id}/items/{item.Id}",
            new { name = "Now depleted", quantity = 0 });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var depleted = await Client.GetFromJsonAsync<ItemResponse>($"/warehouse/{warehouse.Id}/items/{item.Id}");
        Assert.Equal(0, depleted!.Quantity);
        Assert.False(depleted.IsOnStock);

        // ...and back again, to prove it is derived rather than latched.
        await Client.PutCurrentAsync($"/warehouse/{warehouse.Id}/items/{item.Id}",
            new { name = "Restocked", quantity = 3 });

        var restocked = await Client.GetFromJsonAsync<ItemResponse>($"/warehouse/{warehouse.Id}/items/{item.Id}");
        Assert.True(restocked!.IsOnStock);
    }

    [Fact]
    public async Task Creating_a_stock_line_returns_201_and_a_location_that_resolves()
    {
        var warehouse = await CreateWarehouseAsync();

        var response = await Client.PostAsJsonAsync($"/warehouse/{warehouse.Id}/items",
            new { sku = "LOC-1", name = "Located stock", quantity = 2 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var fetched = await Client.GetFromJsonAsync<ItemResponse>(response.Headers.Location);
        Assert.Equal("LOC-1", fetched!.Sku);
    }

    [Fact]
    public async Task A_sku_is_unique_within_a_warehouse_but_free_across_warehouses()
    {
        // Both seeded warehouses already hold PAL-1001, which is the cross-warehouse half.
        var osloItems = await Client.GetFromJsonAsync<List<ItemResponse>>($"/warehouse/{Oslo}/items");
        var bergenItems = await Client.GetFromJsonAsync<List<ItemResponse>>($"/warehouse/{Bergen}/items");

        Assert.Contains(osloItems!, item => item.Sku == "PAL-1001");
        Assert.Contains(bergenItems!, item => item.Sku == "PAL-1001");

        var duplicate = await Client.PostAsJsonAsync($"/warehouse/{Oslo}/items",
            new { sku = "PAL-1001", name = "Duplicate attempt", quantity = 1 });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("Duplicate SKU", problem!.Title);
    }

    [Fact]
    public async Task A_deactivated_warehouse_refuses_new_stock()
    {
        var response = await Client.PostAsJsonAsync($"/warehouse/{DeactivatedTrondheim}/items",
            new { sku = "REFUSED-1", name = "Should not land", quantity = 5 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("Warehouse is not active", problem!.Title);
        Assert.Contains("TRD-01", problem.Detail);
    }

    [Fact]
    public async Task An_unknown_parent_warehouse_is_a_titled_not_found()
    {
        var missing = Guid.NewGuid();

        var response = await Client.GetAsync($"/warehouse/{missing}/items");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("Warehouse not found", problem!.Title);
    }

    [Fact]
    public async Task A_stock_line_cannot_be_reached_through_the_wrong_warehouse()
    {
        var warehouse = await CreateWarehouseAsync();
        var item = await AddItemAsync(warehouse.Id, "SCOPED-1", quantity: 1);

        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/warehouse/{Oslo}/items/{item.Id}")).StatusCode);

        var crossUpdate = await Client.PutIfMatchAsync($"/warehouse/{Oslo}/items/{item.Id}",
            new { name = "Cross parent write", quantity = 99 }, ConditionalRequests.AnyVersion);
        Assert.Equal(HttpStatusCode.NotFound, crossUpdate.StatusCode);

        // The real owner still sees it untouched.
        var untouched = await Client.GetFromJsonAsync<ItemResponse>($"/warehouse/{warehouse.Id}/items/{item.Id}");
        Assert.Equal(1, untouched!.Quantity);
    }

    [Fact]
    public async Task Invalid_stock_lines_are_rejected_per_field()
    {
        var warehouse = await CreateWarehouseAsync();

        var response = await Client.PostAsJsonAsync($"/warehouse/{warehouse.Id}/items",
            new { sku = "lower case", name = "", quantity = -3 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Contains("Sku", problem!.Errors!.Keys);
        Assert.Contains("Name", problem.Errors.Keys);
        Assert.Contains("Quantity", problem.Errors.Keys);
    }

    [Fact]
    public async Task Deleting_a_stock_line_is_idempotent_after_the_first_call()
    {
        var warehouse = await CreateWarehouseAsync();
        var item = await AddItemAsync(warehouse.Id, "GONE-1", quantity: 1);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.DeleteCurrentAsync($"/warehouse/{warehouse.Id}/items/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.DeleteIfMatchAsync($"/warehouse/{warehouse.Id}/items/{item.Id}",
                ConditionalRequests.AnyVersion)).StatusCode);
    }

    [Fact]
    public async Task Removing_a_stock_line_frees_its_sku_for_reuse()
    {
        var warehouse = await CreateWarehouseAsync();
        var item = await AddItemAsync(warehouse.Id, "REUSE-1", quantity: 1);

        await Client.DeleteCurrentAsync($"/warehouse/{warehouse.Id}/items/{item.Id}");

        var again = await Client.PostAsJsonAsync($"/warehouse/{warehouse.Id}/items",
            new { sku = "REUSE-1", name = "Restocked line", quantity = 4 });

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

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
