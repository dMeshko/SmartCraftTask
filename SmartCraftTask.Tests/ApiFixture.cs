using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SmartCraftTask.Data;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Tests;

/// <summary>
/// Boots the real application against a throwaway database on the SQL Server that compose already
/// runs, then drops it. Deliberately not SQLite or the in-memory provider: the behaviour most worth
/// testing here — the IsOnStock computed column and the composite unique index — only exists in
/// SQL Server. Prerequisite: docker compose up -d sql-server.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private const string Server = "tcp:127.0.0.1,6433";
    private const string Credentials = "User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True";
    private const string SigningKey = "test-only-signing-key-not-for-any-real-deployment";

    public const string ManagerUsername = "manager";
    public const string ManagerPassword = "manager-secret";
    public const string OperatorUsername = "operator";
    public const string OperatorPassword = "operator-secret";
    public const string WarehouseViewerUsername = "warehouse-viewer";
    public const string WarehouseViewerPassword = "warehouse-viewer-secret";
    public const string StockViewerUsername = "stock-viewer";
    public const string StockViewerPassword = "stock-viewer-secret";

    private readonly string _databaseName = $"SmartCraftTask_Test_{Guid.NewGuid():N}";

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Unauthenticated. Only /auth/token should answer it.</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <summary>Holds all four roles.</summary>
    public HttpClient Manager { get; private set; } = null!;

    /// <summary>Stock read and write, plus reading warehouses.</summary>
    public HttpClient Operator { get; private set; } = null!;

    /// <summary>Reads warehouses only.</summary>
    public HttpClient WarehouseViewer { get; private set; } = null!;

    /// <summary>Reads stock only.</summary>
    public HttpClient StockViewer { get; private set; } = null!;

    private string ConnectionString => $"Server={Server};Database={_databaseName};{Credentials}";

    public async Task InitializeAsync()
    {
        // Configuration includes environment variables, and these have to be in place before the
        // host is built. Jwt:Key is empty in appsettings.json, so without this the startup
        // validation would fail the whole run — which is the intended behaviour in production.
        Environment.SetEnvironmentVariable("ConnectionString", ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", SigningKey);

        _factory = new WebApplicationFactory<Program>();
        Client = _factory.CreateClient();

        await _factory.Services.MigrateAndSeedAsync();

        Manager = await CreateAuthenticatedClientAsync(ManagerUsername, ManagerPassword);
        Operator = await CreateAuthenticatedClientAsync(OperatorUsername, OperatorPassword);
        WarehouseViewer = await CreateAuthenticatedClientAsync(WarehouseViewerUsername, WarehouseViewerPassword);
        StockViewer = await CreateAuthenticatedClientAsync(StockViewerUsername, StockViewerPassword);
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

        Client.Dispose();
        Manager.Dispose();
        Operator.Dispose();
        WarehouseViewer.Dispose();
        StockViewer.Dispose();
        await _factory.DisposeAsync();
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string username, string password)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/token", new { username, password });
        response.EnsureSuccessStatusCode();

        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);

        return client;
    }

    /// <summary>A warehouse code that no other test will collide with.</summary>
    public static string UniqueCode() => $"T{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
