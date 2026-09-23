using System.Net;
using System.Net.Http.Json;

namespace SmartCraftTask.Tests;

/// <summary>
/// The health endpoints. Both are unauthenticated on purpose — a probe has no token to offer — and
/// the split matters: liveness must not consult the database, or a database outage becomes a
/// restart loop. The unhealthy path is not covered here, because taking the database away from a
/// running host mid-suite would break every other test in the collection; it is verified by hand
/// against the container instead, and the README records what that showed.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class HealthApiTests(ApiFixture fixture)
{
    /// <summary>The client with no Authorization header, exactly as a probe would arrive.</summary>
    private HttpClient Anonymous => fixture.Client;

    [Fact]
    public async Task Liveness_answers_an_anonymous_caller()
    {
        var response = await Anonymous.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<HealthShape>();
        Assert.Equal("Healthy", report!.Status);
    }

    [Fact]
    public async Task Liveness_consults_no_dependency()
    {
        var report = await Anonymous.GetFromJsonAsync<HealthShape>("/health");

        // An empty list is the point: this endpoint reports that the process is serving and
        // nothing else, so it cannot be dragged down by something it does not own.
        Assert.Empty(report!.Checks);
    }

    [Fact]
    public async Task Readiness_answers_an_anonymous_caller_and_names_the_database_check()
    {
        var response = await Anonymous.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<HealthShape>();
        Assert.Equal("Healthy", report!.Status);

        var database = Assert.Single(report.Checks, check => check.Name == "database");
        Assert.Equal("Healthy", database.Status);
    }

    [Fact]
    public async Task Neither_health_endpoint_leaks_diagnostics_a_probe_has_no_business_seeing()
    {
        // The endpoints are unauthenticated, so the body must stay to status and timing. Anything
        // resembling a connection string or a stack frame would be readable by the whole network.
        foreach (var url in new[] { "/health", "/health/ready" })
        {
            var body = await Anonymous.GetStringAsync(url);

            Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_health_endpoints_stay_out_of_the_api_document()
    {
        // They are operational surface, not API surface. A health check endpoint brings no
        // API-explorer metadata, so this holds without being asked to; the test is here because a
        // hand-rolled replacement would show up in the document, and silently.
        var document = await Anonymous.GetFromJsonAsync<System.Text.Json.JsonDocument>("/openapi/v1.json");

        var paths = document!.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name);

        Assert.DoesNotContain(paths, path => path.StartsWith("/health", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Just enough of the health payload to assert on.</summary>
public sealed record HealthShape
{
    public string? Status { get; init; }

    public double TotalDurationMs { get; init; }

    public List<HealthCheckShape> Checks { get; init; } = [];
}

public sealed record HealthCheckShape
{
    public string? Name { get; init; }

    public string? Status { get; init; }

    public double DurationMs { get; init; }
}
