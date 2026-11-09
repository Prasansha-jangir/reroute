using System.Text.Json.Serialization;
using Reroute.BuildingBlocks.Cqrs;
using Reroute.BuildingBlocks.Observability;
using Reroute.BuildingBlocks.Persistence;
using Reroute.BuildingBlocks.Web;
using Reroute.Claims.Api.Endpoints;
using Reroute.Claims.Application.Claims;
using Reroute.Claims.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Composition root: the only place that knows every layer ----
builder.AddServiceDefaults("claims-api");
builder.AddJwtResourceServer();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddCqrs(typeof(FileClaimCommand).Assembly);
builder.Services.AddClaimsInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<ConcurrencyExceptionHandler>();

var app = builder.Build();

// In Kubernetes migrations run as a pre-deploy Job (`--migrate-only`); locally we run them on start for convenience.
if (args.Contains("--migrate-only") || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    var migrator = new SqlMigrator(
        app.Configuration.GetConnectionString("ClaimsDb")!,
        typeof(Reroute.Claims.Infrastructure.DependencyInjection).Assembly,
        app.Logger);
    await migrator.MigrateAsync(createDatabaseIfMissing: !app.Environment.IsProduction());
    if (args.Contains("--migrate-only"))
    {
        return;
    }
}

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsProduction())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // interactive API docs at /scalar
}

app.MapServiceDefaults();
app.MapClaimsEndpoints();

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
