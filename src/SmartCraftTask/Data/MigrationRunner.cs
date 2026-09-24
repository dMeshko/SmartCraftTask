using Microsoft.EntityFrameworkCore;

namespace SmartCraftTask.Data;

/// <summary>
/// Preparing the database is a deployment step with its own process, not something every replica
/// does on its way up.
/// </summary>
/// <remarks>
/// Run the service with <c>--migrate</c> and it brings the schema up to date, plants the sample
/// aggregates if the database is empty, and exits. Nothing else happens: no web host, no
/// authentication, no rate limiter. That is the point — this path needs a connection string and
/// nothing else, so the container that runs it never sees a signing key.
/// </remarks>
public static class MigrationRunner
{
    public const string Switch = "--migrate";

    /// <summary>
    /// The same instruction as <see cref="Switch"/>, given through the environment.
    /// </summary>
    /// <remarks>
    /// Both exist because an argument is easy to lose. Compose merges a service's
    /// <c>environment</c> map with any override applied on top of it, but an <c>entrypoint</c>
    /// override replaces the whole thing, arguments included — which is exactly what Rider's
    /// generated compose override does to every service in the file, turning the migration
    /// container into a second copy of the web host that never exits. Read from the environment,
    /// the instruction survives that.
    /// </remarks>
    public const string EnvironmentVariable = "MIGRATE_DATABASE";

    private const string LoggerCategory = "SmartCraftTask.Migrations";

    public static bool IsRequested(string[] args) =>
        args.Contains(Switch, StringComparer.OrdinalIgnoreCase)
        || string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The process exit code: 0 when the database is ready, 1 when it is not.</summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetValue<string>("ConnectionString")));

        using var host = builder.Build();

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);

        try
        {
            await host.Services.MigrateAndSeedAsync(cancellationToken);

            logger.LogInformation("The database is up to date.");

            return 0;
        }
        catch (Exception exception)
        {
            // A non-zero exit is what stops the deployment: compose waits for this container to
            // complete successfully before starting the API, so a failure here holds the API back
            // rather than letting it serve against a schema that was never applied.
            logger.LogCritical(exception, "Preparing the database failed. The schema may be incomplete.");

            return 1;
        }
    }
}
