using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SmartCraftTask.Data;

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

    private readonly string _databaseName = $"SmartCraftTask_Test_{Guid.NewGuid():N}";

    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    private string ConnectionString => $"Server={Server};Database={_databaseName};{Credentials}";

    public async Task InitializeAsync()
    {
        // Program reads the flat "ConnectionString" key and configuration includes environment
        // variables, so this has to be set before the host is built.
        Environment.SetEnvironmentVariable("ConnectionString", ConnectionString);

        _factory = new WebApplicationFactory<Program>();
        Client = _factory.CreateClient();

        // Idempotent, and explicit rather than relying on the host having run it during startup.
        await _factory.Services.MigrateAndSeedAsync();
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
        await _factory.DisposeAsync();
    }

    /// <summary>A warehouse code that no other test will collide with.</summary>
    public static string UniqueCode() => $"T{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
