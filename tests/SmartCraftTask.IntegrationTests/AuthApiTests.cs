using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class AuthApiTests(ApiFixture fixture)
{
    private static readonly Guid Oslo = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task A_manager_token_carries_every_role()
    {
        var token = await IssueAsync(ApiFixture.ManagerUsername, ApiFixture.ManagerPassword);

        Assert.Equal("Bearer", token.TokenType);
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(3, token.AccessToken.Split('.').Length);
        Assert.Equal(
            ["StockOperator", "StockReader", "WarehouseManager", "WarehouseReader"],
            token.Roles.OrderBy(role => role).ToArray());
    }

    [Fact]
    public async Task A_viewer_token_carries_exactly_the_one_role()
    {
        var warehouseViewer = await IssueAsync(ApiFixture.WarehouseViewerUsername, ApiFixture.WarehouseViewerPassword);
        var stockViewer = await IssueAsync(ApiFixture.StockViewerUsername, ApiFixture.StockViewerPassword);

        Assert.Equal(["WarehouseReader"], warehouseViewer.Roles.ToArray());
        Assert.Equal(["StockReader"], stockViewer.Roles.ToArray());
    }

    [Fact]
    public async Task The_token_endpoint_is_the_one_door_open_to_anonymous_callers()
    {
        // It answers without credentials attached...
        var token = await fixture.Client.PostAsJsonAsync("/auth/token", new
        {
            username = ApiFixture.ManagerUsername,
            password = ApiFixture.ManagerPassword
        });
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);

        // ...and nothing else does, reads included.
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync("/warehouse")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync($"/warehouse/{Oslo}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync($"/warehouse/{Oslo}/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await fixture.Client.PostAsJsonAsync("/warehouse", NewWarehouse())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.DeleteAsync($"/warehouse/{Oslo}")).StatusCode);
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
    public async Task A_warehouse_viewer_reads_warehouses_and_nothing_else()
    {
        var client = fixture.WarehouseViewer;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/warehouse")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/warehouse/{Oslo}")).StatusCode);

        // Stock is a different area, and reading it is a different role.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/warehouse/{Oslo}/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/warehouse", NewWarehouse())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/warehouse/{Oslo}")).StatusCode);
    }

    [Fact]
    public async Task A_stock_viewer_reads_stock_and_nothing_else()
    {
        var client = fixture.StockViewer;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/warehouse/{Oslo}/items")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/warehouse")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/warehouse/{Oslo}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/warehouse/{Oslo}/items",
            new { sku = "VIEW-1", name = "Not allowed", quantity = 1 })).StatusCode);
    }

    [Fact]
    public async Task An_operator_moves_stock_and_can_see_the_warehouses_holding_it()
    {
        var warehouse = await CreateWarehouseAsync();

        Assert.Equal(HttpStatusCode.OK, (await fixture.Operator.GetAsync("/warehouse")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Operator.GetAsync($"/warehouse/{warehouse.Id}/items")).StatusCode);

        var added = await fixture.Operator.PostAsJsonAsync($"/warehouse/{warehouse.Id}/items",
            new { sku = "OPS-1", name = "Operator stock", quantity = 4 });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var item = (await added.Content.ReadFromJsonAsync<ItemResponse>())!;

        Assert.Equal(HttpStatusCode.NoContent,
            (await fixture.Operator.PutCurrentAsync($"/warehouse/{warehouse.Id}/items/{item.Id}",
                new { name = "Operator stock, fewer", quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await fixture.Operator.DeleteCurrentAsync($"/warehouse/{warehouse.Id}/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task An_operator_may_not_run_the_warehouses_themselves()
    {
        Assert.Equal(HttpStatusCode.Forbidden,
            (await fixture.Operator.PostAsJsonAsync("/warehouse", NewWarehouse())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Operator.DeleteAsync($"/warehouse/{Oslo}")).StatusCode);

        // And the warehouse is still there.
        Assert.Equal(HttpStatusCode.OK, (await fixture.Manager.GetAsync($"/warehouse/{Oslo}")).StatusCode);
    }

    [Fact]
    public async Task A_manager_may_do_everything()
    {
        var warehouse = await CreateWarehouseAsync();

        Assert.Equal(HttpStatusCode.OK, (await fixture.Manager.GetAsync("/warehouse")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Manager.GetAsync($"/warehouse/{warehouse.Id}/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await fixture.Manager.PostAsJsonAsync($"/warehouse/{warehouse.Id}/items",
                new { sku = "MGR-1", name = "Manager stock", quantity = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await fixture.Manager.DeleteCurrentAsync($"/warehouse/{warehouse.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_openapi_document_secures_everything_except_the_token_endpoint()
    {
        var document = await fixture.Client.GetFromJsonAsync<System.Text.Json.JsonDocument>("/openapi/v1.json");
        var root = document!.RootElement;

        Assert.True(root.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));

        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var where = $"{operation.Name.ToUpperInvariant()} {path.Name}";
                var secured = operation.Value.TryGetProperty("security", out var security);

                if (path.Name == "/auth/token")
                {
                    Assert.False(secured, $"{where} should be open to anonymous callers.");
                    continue;
                }

                Assert.True(secured, $"{where} declares no security.");

                // Presence is not enough: a requirement that names no scheme serialises as [{}],
                // which Swagger UI reads as "secured by nothing" and sends no token for.
                var requirements = security.EnumerateArray().ToList();
                Assert.NotEmpty(requirements);
                Assert.All(requirements, requirement =>
                {
                    var schemes = requirement.EnumerateObject().Select(scheme => scheme.Name).ToList();
                    Assert.Contains("Bearer", schemes);
                });
            }
        }
    }

    [Fact]
    public async Task The_openapi_document_declares_If_Match_on_exactly_the_operations_that_demand_it()
    {
        var document = await fixture.Client.GetFromJsonAsync<System.Text.Json.JsonDocument>("/openapi/v1.json");
        var root = document!.RootElement;

        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var where = $"{operation.Name.ToUpperInvariant()} {path.Name}";

                var demandsPrecondition = operation.Value.GetProperty("responses")
                    .EnumerateObject()
                    .Any(response => response.Name == "428");

                var declaresHeader = operation.Value.TryGetProperty("parameters", out var parameters)
                    && parameters.EnumerateArray().Any(parameter =>
                        parameter.GetProperty("name").GetString() == "If-Match"
                        && parameter.GetProperty("in").GetString() == "header");

                Assert.True(demandsPrecondition == declaresHeader,
                    $"{where}: answers 428 = {demandsPrecondition}, but declares the If-Match header = {declaresHeader}. "
                    + "Swagger UI only offers a box for headers the document names.");
            }
        }
    }

    private async Task<TokenResponse> IssueAsync(string username, string password)
    {
        var response = await fixture.Client.PostAsJsonAsync("/auth/token", new { username, password });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private async Task<WarehouseResponse> CreateWarehouseAsync()
    {
        var response = await fixture.Manager.PostAsJsonAsync("/warehouse", NewWarehouse());
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<WarehouseResponse>())!;
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
