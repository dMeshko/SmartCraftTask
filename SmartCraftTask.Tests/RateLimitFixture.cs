using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SmartCraftTask.Data;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Tests;

/// <summary>
/// A second host, with the limits turned down to numbers a test can reach in a few requests. The
/// shared <see cref="ApiFixture"/> lifts them instead, because nothing else in the suite is about
/// rate limiting and a realistic limit would fail all of it.
/// </summary>
/// <remarks>
/// Both budgets are deliberately larger than any single test needs, and the two authenticated
/// clients are created here rather than inside a test, so no test depends on running before
/// another: xUnit does not order methods within a class, and every budget here only shrinks.
/// </remarks>
public sealed class RateLimitFixture : IAsyncLifetime
{
    public const int PermitLimit = 5;
    public const int TokenPermitLimit = 5;

    private const string Server = "tcp:127.0.0.1,6433";
    private const string Credentials = "User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True";
    private const string SigningKey = "test-only-signing-key-not-for-any-real-deployment";

    private readonly string _databaseName = $"SmartCraftTask_RateLimit_{Guid.NewGuid():N}";

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>No token. Used for the probe and token-issuance requests.</summary>
    public HttpClient Anonymous { get; private set; } = null!;

    public HttpClient Manager { get; private set; } = null!;

    /// <summary>A second identity, to show that a budget belongs to a user rather than to the host.</summary>
    public HttpClient Operator { get; private set; } = null!;

    private string ConnectionString => $"Server={Server};Database={_databaseName};{Credentials}";

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionString", ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", SigningKey);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RateLimiting:PermitLimit"] = PermitLimit.ToString(),
                    ["RateLimiting:WindowSeconds"] = "60",
                    ["RateLimiting:TokenPermitLimit"] = TokenPermitLimit.ToString(),
                    ["RateLimiting:TokenWindowSeconds"] = "60"
                })));

        Anonymous = _factory.CreateClient();

        await _factory.Services.MigrateAndSeedAsync();

        Manager = await AuthenticateAsync(ApiFixture.ManagerUsername, ApiFixture.ManagerPassword);
        Operator = await AuthenticateAsync(ApiFixture.OperatorUsername, ApiFixture.OperatorPassword);
    }

    public async Task DisposeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using (var context = new ApplicationDbContext(options))
        {
            await context.Database.EnsureDeletedAsync();
        }

        Anonymous.Dispose();
        Manager.Dispose();
        Operator.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<HttpClient> AuthenticateAsync(string username, string password)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/token", new { username, password });
        response.EnsureSuccessStatusCode();

        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);

        return client;
    }
}
