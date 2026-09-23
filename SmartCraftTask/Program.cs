using System.Reflection;
using FluentValidation;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartCraftTask.Auth;
using SmartCraftTask.Data;
using SmartCraftTask.Filters;
using SmartCraftTask.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(options => options.Filters.Add<FluentValidationFilter>());
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
    options.AddOperationTransformer<IfMatchHeaderTransformer>();
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "SmartCraftTask Warehouse API";
        document.Info.Description =
            "Warehouses and the stock lines they hold. Stock lines are addressed through their "
            + "warehouse, because they belong to it and cannot exist on their own.";

        return Task.CompletedTask;
    });
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetValue<string>("ConnectionString")));

// "ready" separates the checks a readiness probe runs from the liveness probe, which runs none.
const string ReadyTag = "ready";

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>(
        name: "database",
        tags: [ReadyTag],
        // Opening the connection is the same work the default test does, but the default runs it
        // through CanConnectAsync, which swallows the failure and reports a bare "Unhealthy" —
        // leaving the log line with no reason in it. Letting the exception out hands it to the
        // health check service, which logs it with the reason attached.
        customTestQuery: async (dbContext, cancellationToken) =>
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            await dbContext.Database.CloseConnectionAsync();

            return true;
        });

// A backstop so no check can hang a probe indefinitely. It only bites on work that observes its
// cancellation token: SqlClient takes the token but does not abandon a connection attempt already
// in flight, so what actually bounds an unreachable database is `Connect Timeout` in the
// connection string, set to 3 seconds. That is deliberately inside this cap, which leaves the
// database's own exception — the one that says *why* — as the thing that gets logged.
builder.Services.Configure<HealthCheckServiceOptions>(options =>
{
    foreach (var registration in options.Registrations)
    {
        registration.Timeout = TimeSpan.FromSeconds(5);
    }
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DevUserStore>();
builder.Services.AddSingleton<TokenService>();

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Jwt:Issuer is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Jwt:Audience is required.")
    .Validate(options => options.HasUsableKey(), $"Jwt:Key must be at least {JwtOptions.MinimumKeyBytes} bytes.")
    .ValidateOnStart();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the short claim names the token actually carries instead of rewriting them
        // to the long WS-Federation URIs.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtOptions.SigningKey(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = ClaimNames.Role
        };
    });

// Every endpoint needs an authenticated caller unless it opts out with [AllowAnonymous],
// so a new action is closed by default rather than open by oversight.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.ManageWarehouses, policy => policy.RequireRole(Roles.WarehouseManager))
    .AddPolicy(Policies.ReadWarehouses, policy => policy.RequireRole(Roles.WarehouseReader))
    .AddPolicy(Policies.ManageStock, policy => policy.RequireRole(Roles.StockOperator))
    .AddPolicy(Policies.ReadStock, policy => policy.RequireRole(Roles.StockReader));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// Mapster reads its rules from the IRegister implementations in this assembly. Registering
// GlobalSettings itself keeps the injected IMapper and the ProjectToType query extension in sync.
var mapsterConfig = TypeAdapterConfig.GlobalSettings;
mapsterConfig.Scan(Assembly.GetExecutingAssembly());
builder.Services.AddSingleton(mapsterConfig);
builder.Services.AddScoped<IMapper, ServiceMapper>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();

    // The document comes from the built-in generator; this is Swashbuckle's UI only.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "SmartCraftTask Warehouse API v1");
        options.DocumentTitle = "SmartCraftTask API";
    });
}

// No HTTPS redirection: the container listens on HTTP only and TLS terminates at the ingress
// in front of it. Left in, the middleware cannot find an HTTPS port and logs a warning on every
// start while doing nothing. Reinstate it, with forwarded headers, if the app ever terminates TLS.
app.UseAuthentication();
app.UseAuthorization();

// Operational surface, not API surface: neither endpoint appears in the OpenAPI document, because
// a health check endpoint carries no API-explorer metadata to put there. HealthApiTests guards
// that, since a future hand-rolled endpoint would not come with the same silence.
//
// The paths are named after the Kubernetes probes that would call them, rather than one of them
// being a bare /health whose meaning a reader has to already know.
//
// Liveness: is this process up and serving? It consults no dependency on purpose. A database
// outage is not a reason to restart the container, and a liveness probe that fails on one turns
// an outage into a restart loop that cannot fix it.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthReportWriter.WriteAsync
}).AllowAnonymous();

// Readiness: should this instance be sent traffic? This one does consult the database, so it
// answers 503 while the database is unreachable even though the process is fine.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(ReadyTag),
    ResponseWriter = HealthReportWriter.WriteAsync
}).AllowAnonymous();

app.MapControllers();

await app.Services.MigrateAndSeedAsync();

app.Run();
