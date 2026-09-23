using System.Net;
using System.Net.Http.Json;
using SmartCraftTask.Dtos;
using SmartCraftTask.Models;

namespace SmartCraftTask.IntegrationTests;

/// <summary>
/// What the API says when something goes wrong: the shape, the status, and what it declines to say.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class ErrorHandlingApiTests(ApiFixture fixture)
{
    private HttpClient Client => fixture.Manager;

    [Fact]
    public async Task An_unhandled_exception_answers_problem_json_and_never_a_stack_trace()
    {
        // Worth pinning because the guard is easy to remove by accident: WebApplication adds the
        // developer exception page in Development, and only the explicit UseExceptionHandler sitting
        // inside it keeps HTML stack traces off the wire. Compose runs Development, so this is the
        // container's behaviour too, not only a production concern.
        var response = await Client.GetAsync("/test-only/throw");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Deliberate failure", body, StringComparison.Ordinal);

        // A trace id instead, so the caller can quote something the logs can be searched by.
        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal(500, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task The_throwing_route_is_test_scaffolding_and_not_part_of_the_api()
    {
        // It comes from this assembly, not the application's, and is hidden from the document. If it
        // ever shows up here, something has made test scaffolding part of the published surface.
        var document = await Client.GetFromJsonAsync<System.Text.Json.JsonDocument>("/openapi/v1.json");

        var paths = document!.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name);

        Assert.DoesNotContain(paths, path => path.StartsWith("/test-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Losing_the_race_on_a_unique_code_is_a_conflict_not_a_server_error()
    {
        // Deterministic rather than a burst of concurrent requests. The row is inserted in a
        // transaction that is still open, so the controller's duplicate check cannot see it — the
        // database has READ_COMMITTED_SNAPSHOT on, so that check reads the snapshot instead of
        // blocking — while the INSERT that follows must wait on the unique index and then fail.
        // That is the race, arranged rather than hoped for: firing concurrent requests at the
        // in-memory host does not reproduce it, because they interleave too little and the
        // duplicate check catches every one of them.
        var code = ApiFixture.UniqueCode();

        await using var context = fixture.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.Warehouses.Add(Warehouse.Register(
            code,
            $"Holder of {code}",
            new Address { Street = "S 1", PostalCode = "1000", City = "Oslo", Country = "Norway" },
            10));

        await context.SaveChangesAsync();

        var contested = Client.PostAsJsonAsync("/warehouse", new
        {
            code,
            name = $"Contender for {code}",
            address = new { street = "S 2", postalCode = "1000", city = "Oslo", country = "Norway" },
            capacityInPallets = 5
        });

        // Long enough for the request to get past its duplicate check and block on the index.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await transaction.CommitAsync();

        var response = await contested;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // The handler's title, not the controller pre-check's "Duplicate warehouse code": this
        // conflict was found by the database, which is the whole point of the test.
        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("Duplicate value", problem!.Title);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_refused_without_naming_dotnet_internals()
    {
        var response = await Client.PostAsync("/warehouse",
            new StringContent("{\"code\": \"BAD\", oops}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        // The deserializer's own message names a .NET type and a byte offset into the body. Neither
        // is something a caller can act on, and both describe the server rather than the request.
        Assert.DoesNotContain("BytePositionInLine", body, StringComparison.Ordinal);
        Assert.DoesNotContain("LineNumber", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.NotNull(problem!.Errors);
        Assert.Contains("body", problem.Errors.Keys);
    }

    [Fact]
    public async Task A_field_of_the_wrong_type_names_the_field_and_nothing_else()
    {
        var response = await Client.PostAsync("/warehouse",
            new StringContent(
                "{\"code\":\"X\",\"name\":\"Y\",\"capacityInPallets\":\"not-a-number\"}",
                System.Text.Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.NotNull(problem!.Errors);

        // The field is the useful half of the deserializer's message, so it is kept; "System.Int32"
        // is the half that is not.
        Assert.Contains("capacityInPallets", problem.Errors.Keys);
        Assert.DoesNotContain("System.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rules_the_project_wrote_itself_are_passed_through_word_for_word()
    {
        // Sanitising the deserializer's messages must not touch the FluentValidation ones, which
        // were written to be read by whoever sent the request.
        var response = await Client.PostAsJsonAsync("/warehouse", new
        {
            code = "lower-case",
            name = "X",
            address = new { street = "S", postalCode = "1", city = "C", country = "N" },
            capacityInPallets = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.NotNull(problem!.Errors);
        Assert.Contains(
            problem.Errors.SelectMany(error => error.Value),
            message => message.Contains("Code may only contain upper-case letters", StringComparison.Ordinal));
    }
}
