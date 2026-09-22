using System.Reflection;
using FluentValidation;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using SmartCraftTask.Data;
using SmartCraftTask.Filters;
using SmartCraftTask.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(options => options.Filters.Add<FluentValidationFilter>());
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "SmartCraftTask Warehouse API";
    document.Info.Description =
        "Warehouses and the stock lines they hold. Stock lines are addressed through their "
        + "warehouse, because they belong to it and cannot exist on their own.";

    return Task.CompletedTask;
}));

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetValue<string>("ConnectionString")));

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

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
    app.MapOpenApi();

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
app.UseAuthorization();

app.MapControllers();

await app.Services.MigrateAndSeedAsync();

app.Run();
