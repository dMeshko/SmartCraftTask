using System.Net;
using System.Net.Http.Json;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.IntegrationTests;

/// <summary>
/// The read half of the ETag story. <see cref="ConcurrencyApiTests"/> covers the write half, where
/// the same tag is sent back as <c>If-Match</c> to stop a blind overwrite; here it is offered as
/// <c>If-None-Match</c> to avoid being sent a body the caller already holds.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class ConditionalReadApiTests(ApiFixture fixture)
{
    private static readonly Guid Oslo = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private HttpClient Client => fixture.Manager;

    [Fact]
    public async Task Offering_the_current_version_is_answered_with_304_and_no_body()
    {
        var url = $"/warehouse/{Oslo}";
        var etag = await Client.GetETagAsync(url);

        var response = await Client.GetIfNoneMatchAsync(url, etag);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);

        // The point of the exercise: nothing on the wire that the caller already has.
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_304_still_carries_the_version_it_is_talking_about()
    {
        // Without it a cache has nothing to store the response against, and RFC 9110 asks for it.
        var url = $"/warehouse/{Oslo}";
        var etag = await Client.GetETagAsync(url);

        var response = await Client.GetIfNoneMatchAsync(url, etag);

        Assert.Equal(etag, response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task A_version_that_is_no_longer_current_is_answered_in_full()
    {
        var url = $"/warehouse/{Oslo}";
        var stale = ConditionalRequests.StaleVersionOf(await Client.GetETagAsync(url));

        var response = await Client.GetIfNoneMatchAsync(url, stale);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var warehouse = await response.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.Equal("OSL-01", warehouse!.Code);
    }

    [Fact]
    public async Task The_weak_spelling_of_our_own_tag_is_still_recognised()
    {
        // If-None-Match compares weakly, so a cache in between may hand our strong tag back with a
        // W/ on the front. Refusing to recognise it would mean sending a body for no reason.
        var url = $"/warehouse/{Oslo}";
        var etag = await Client.GetETagAsync(url);

        var response = await Client.GetIfNoneMatchAsync(url, $"W/{etag}");

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task A_caller_holding_several_versions_may_offer_all_of_them()
    {
        var url = $"/warehouse/{Oslo}";
        var etag = await Client.GetETagAsync(url);

        var response = await Client.GetIfNoneMatchAsync(
            url, $"{ConditionalRequests.StaleVersionOf(etag)}, {etag}");

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task A_wildcard_asks_only_whether_the_resource_exists()
    {
        var response = await Client.GetIfNoneMatchAsync($"/warehouse/{Oslo}", "*");

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task A_wildcard_against_something_that_does_not_exist_is_still_a_404()
    {
        // The condition is about the version of a resource, so it cannot turn a missing one into a
        // 304 — that would tell the caller their stale copy is current.
        var response = await Client.GetIfNoneMatchAsync($"/warehouse/{Guid.NewGuid()}", "*");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_write_moves_the_version_on_and_the_next_read_is_answered_in_full_again()
    {
        var created = await CreateWarehouseAsync();
        var url = $"/warehouse/{created.Id}";

        var before = await Client.GetETagAsync(url);
        Assert.Equal(HttpStatusCode.NotModified, (await Client.GetIfNoneMatchAsync(url, before)).StatusCode);

        var written = await Client.PutCurrentAsync(url, new
        {
            name = "Renamed",
            address = new { street = "Bryggen 12", postalCode = "5003", city = "Bergen", country = "Norway" },
            capacityInPallets = 250,
            isActive = true
        });
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);

        // Same tag, different answer: the resource moved underneath it.
        Assert.Equal(HttpStatusCode.OK, (await Client.GetIfNoneMatchAsync(url, before)).StatusCode);

        var after = await Client.GetETagAsync(url);
        Assert.NotEqual(before, after);
        Assert.Equal(HttpStatusCode.NotModified, (await Client.GetIfNoneMatchAsync(url, after)).StatusCode);
    }

    [Fact]
    public async Task Stock_lines_answer_conditional_reads_with_their_own_version()
    {
        var warehouse = await CreateWarehouseAsync();

        var created = await Client.PostAsJsonAsync($"/warehouse/{warehouse.Id}/items",
            new { sku = "COND-1", name = "Conditional stock", quantity = 3 });
        created.EnsureSuccessStatusCode();
        var item = (await created.Content.ReadFromJsonAsync<ItemResponse>())!;

        var url = $"/warehouse/{warehouse.Id}/items/{item.Id}";
        var etag = await Client.GetETagAsync(url);

        Assert.Equal(HttpStatusCode.NotModified, (await Client.GetIfNoneMatchAsync(url, etag)).StatusCode);

        // An item's version is its own, so its warehouse's tag says nothing about it.
        var warehouseETag = await Client.GetETagAsync($"/warehouse/{warehouse.Id}");
        Assert.Equal(HttpStatusCode.OK, (await Client.GetIfNoneMatchAsync(url, warehouseETag)).StatusCode);
    }

    [Fact]
    public async Task The_document_offers_If_None_Match_on_exactly_the_reads_that_answer_304()
    {
        var document = await Client.GetFromJsonAsync<System.Text.Json.JsonDocument>("/openapi/v1.json");

        foreach (var path in document!.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var where = $"{operation.Name.ToUpperInvariant()} {path.Name}";

                var answers304 = operation.Value.GetProperty("responses")
                    .EnumerateObject()
                    .Any(response => response.Name == "304");

                var declaresHeader = operation.Value.TryGetProperty("parameters", out var parameters)
                    && parameters.EnumerateArray().Any(parameter =>
                        parameter.GetProperty("name").GetString() == "If-None-Match"
                        && parameter.GetProperty("in").GetString() == "header");

                Assert.True(answers304 == declaresHeader,
                    $"{where}: answers 304 = {answers304}, declares If-None-Match = {declaresHeader}.");
            }
        }
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
}
