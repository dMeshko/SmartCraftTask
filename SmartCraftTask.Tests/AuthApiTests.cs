using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Tests;

[Collection(nameof(ApiCollection))]
public sealed class AuthApiTests(ApiFixture fixture)
{
    private static readonly Guid Oslo = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Valid_credentials_yield_a_bearer_token_carrying_the_role()
    {
        var response = await fixture.Client.PostAsJsonAsync("/auth/token", new
        {
            username = ApiFixture.ManagerUsername,
            password = ApiFixture.ManagerPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal("WarehouseManager", token.Role);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);
        // header.payload.signature
        Assert.Equal(3, token.AccessToken.Split('.').Length);
    }

    [Fact]
    public async Task A_wrong_password_is_unauthorized_and_says_nothing_useful()
    {
        var response = await fixture.Client.PostAsJsonAsync("/auth/token", new
        {
            username = ApiFixture.ManagerUsername,
            password = "not-the-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("Invalid credentials", problem!.Title);
        // The message covers both halves rather than revealing which one was wrong.
        Assert.Contains("username or password", problem.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no such user", problem.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_credential_is_a_validation_error_not_an_auth_failure()
    {
        var response = await fixture.Client.PostAsJsonAsync("/auth/token", new { username = "", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Contains("Username", problem!.Errors!.Keys);
        Assert.Contains("Password", problem.Errors.Keys);
    }

    [Fact]
    public async Task Reads_stay_open_to_anyone()
    {
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("/warehouse")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync($"/warehouse/{Oslo}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync($"/warehouse/{Oslo}/items")).StatusCode);
    }

    [Fact]
    public async Task Writes_without_a_token_are_unauthorized()
    {
        var warehouse = await fixture.Client.PostAsJsonAsync("/warehouse", NewWarehouse());
        Assert.Equal(HttpStatusCode.Unauthorized, warehouse.StatusCode);

        var item = await fixture.Client.PostAsJsonAsync($"/warehouse/{Oslo}/items",
            new { sku = "NOAUTH-1", name = "No token", quantity = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, item.StatusCode);

        var delete = await fixture.Client.DeleteAsync($"/warehouse/{Oslo}");
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    [Fact]
    public async Task A_garbled_token_is_unauthorized_rather_than_a_server_error()
    {
        // Per-request header: a bare HttpClient would bypass the in-memory test server entirely.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/warehouse")
        {
            Content = JsonContent.Create(NewWarehouse()),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token") }
        };

        var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_operator_may_move_stock()
    {
        var created = await fixture.Manager.PostAsJsonAsync("/warehouse", NewWarehouse());
        var warehouse = (await created.Content.ReadFromJsonAsync<WarehouseResponse>())!;

        var added = await fixture.Operator.PostAsJsonAsync($"/warehouse/{warehouse.Id}/items",
            new { sku = "OPS-1", name = "Operator stock", quantity = 4 });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var item = (await added.Content.ReadFromJsonAsync<ItemResponse>())!;

        var updated = await fixture.Operator.PutAsJsonAsync($"/warehouse/{warehouse.Id}/items/{item.Id}",
            new { name = "Operator stock, fewer", quantity = 1 });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        var removed = await fixture.Operator.DeleteAsync($"/warehouse/{warehouse.Id}/items/{item.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }

    [Fact]
    public async Task An_operator_may_not_run_the_warehouses_themselves()
    {
        var created = await fixture.Operator.PostAsJsonAsync("/warehouse", NewWarehouse());
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);

        var deleted = await fixture.Operator.DeleteAsync($"/warehouse/{Oslo}");
        Assert.Equal(HttpStatusCode.Forbidden, deleted.StatusCode);

        // And the warehouse is still there.
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync($"/warehouse/{Oslo}")).StatusCode);
    }

    [Fact]
    public async Task A_manager_may_do_both()
    {
        var created = await fixture.Manager.PostAsJsonAsync("/warehouse", NewWarehouse());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var warehouse = (await created.Content.ReadFromJsonAsync<WarehouseResponse>())!;

        var added = await fixture.Manager.PostAsJsonAsync($"/warehouse/{warehouse.Id}/items",
            new { sku = "MGR-1", name = "Manager stock", quantity = 2 });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Manager.DeleteAsync($"/warehouse/{warehouse.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_openapi_document_marks_writes_as_secured_and_leaves_reads_open()
    {
        var document = await fixture.Client.GetFromJsonAsync<System.Text.Json.JsonDocument>("/openapi/v1.json");
        var root = document!.RootElement;

        Assert.True(root.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));

        var warehouse = root.GetProperty("paths").GetProperty("/warehouse");
        Assert.False(warehouse.GetProperty("get").TryGetProperty("security", out _));
        Assert.True(warehouse.GetProperty("post").TryGetProperty("security", out _));
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
}
