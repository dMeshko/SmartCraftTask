using System.Net;
using System.Net.Http.Json;

namespace SmartCraftTask.Tests;

/// <summary>
/// Rate limiting, against its own host with small budgets. In <see cref="ApiCollection"/> so it does
/// not run beside the shared fixture: both set the connection string through the environment, which
/// is process-wide.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class RateLimitApiTests(RateLimitFixture limits) : IClassFixture<RateLimitFixture>
{
    [Fact]
    public async Task A_caller_over_its_budget_is_refused_with_429_and_told_when_to_return()
    {
        HttpResponseMessage? refused = null;

        for (var attempt = 1; attempt <= RateLimitFixture.PermitLimit + 1; attempt++)
        {
            var response = await limits.Manager.GetAsync("/warehouse");

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                refused = response;
                break;
            }

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.NotNull(refused);

        // Retry-After is the difference between a client that backs off and one that hammers.
        Assert.True(refused.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.Equal(RateLimitFixture.GlobalRetryAfter.ToString(), Assert.Single(retryAfter));

        // The same RFC 9457 shape as every other failure, not a bare status code.
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("Too many requests", problem!.Title);
        Assert.Equal(429, problem.Status);
    }

    [Fact]
    public async Task A_budget_belongs_to_a_user_rather_than_to_the_whole_host()
    {
        // Spend the manager's budget, however much of it is left.
        for (var attempt = 1; attempt <= RateLimitFixture.PermitLimit + 1; attempt++)
        {
            await limits.Manager.GetAsync("/warehouse");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await limits.Manager.GetAsync("/warehouse")).StatusCode);

        // A different identity has its own, untouched. Partitioning by host or by address would
        // have let one busy colleague throttle everyone sharing the office connection.
        Assert.Equal(HttpStatusCode.OK, (await limits.Operator.GetAsync("/warehouse")).StatusCode);
    }

    [Fact]
    public async Task Token_issuance_is_limited_even_though_the_caller_has_no_identity_yet()
    {
        // Counted by address, because a caller guessing passwords has no user to count against.
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 1; attempt <= RateLimitFixture.TokenPermitLimit + 1; attempt++)
        {
            var response = await limits.Anonymous.PostAsJsonAsync("/auth/token",
                new { username = ApiFixture.ManagerUsername, password = "wrong-on-purpose" });

            statuses.Add(response.StatusCode);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Which limiter refused this matters: the global one counts these requests too, and
                // a 429 from it would prove nothing about the token policy. The two are configured
                // with different windows precisely so Retry-After can tell them apart.
                Assert.Equal(
                    RateLimitFixture.TokenWindowSeconds.ToString(),
                    Assert.Single(response.Headers.GetValues("Retry-After")));
                break;
            }

            // Wrong credentials, so 401 until the budget runs out. Failed attempts count: if they
            // did not, the limit would be no obstacle at all to guessing.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task A_request_that_authorisation_refuses_still_spends_budget()
    {
        // Pins where UseRateLimiter sits. It runs between authentication and authorisation, so a
        // caller holding a valid token and the wrong role is counted before it is turned away. Move
        // it after authorisation and every one of these is a free 403: the caller is refused
        // without ever spending a permit, and a flood costs them nothing and the service everything.
        var refused = false;

        for (var attempt = 1; attempt <= RateLimitFixture.PermitLimit + 1; attempt++)
        {
            var response = await limits.StockViewer.GetAsync("/warehouse");

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                refused = true;
                break;
            }

            // StockReader may not read warehouses, so this is the authorisation refusal itself.
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.True(refused, "a forbidden request should still consume the caller's budget");
    }

    [Fact]
    public async Task Probes_are_never_throttled()
    {
        // Well past the budget. A probe refused with a 429 is indistinguishable from an unhealthy
        // instance, and being throttled into a restart is a poor way to find out the limit is low.
        for (var attempt = 1; attempt <= (RateLimitFixture.PermitLimit * 2) + 2; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await limits.Anonymous.GetAsync("/health/live")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await limits.Anonymous.GetAsync("/health/ready")).StatusCode);
        }
    }
}
