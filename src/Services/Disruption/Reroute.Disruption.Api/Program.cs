using Reroute.BuildingBlocks.Messaging;
using Reroute.BuildingBlocks.Observability;
using Reroute.BuildingBlocks.Web;
using Reroute.Contracts;
using Reroute.Disruption.Api.Detection;
using Reroute.Disruption.Api.Simulation;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("disruption-api");
builder.AddJwtResourceServer();

var redis = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
builder.Services.AddSingleton<FlightStateStore>();
builder.Services.AddHealthChecks().AddRedis(redis, name: "redis");

builder.Services.AddKafkaProducer(builder.Configuration);
builder.Services.AddKafkaConsumer<FlightStatusUpdate, FlightStatusHandler>(Topics.FlightStatusRaw, groupId: "disruption-detector");

builder.Services.Configure<SimulatorOptions>(builder.Configuration.GetSection("Simulator"));
builder.Services.AddHostedService<FlightStatusSimulator>();

var app = builder.Build();
app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapServiceDefaults();

var flights = app.MapGroup("/api/flights").RequireAuthorization(Policies.Traveller).WithTags("Flights");

flights.MapGet("/{flightKey}/status", async (string flightKey, FlightStateStore store) =>
    await store.GetAsync(flightKey) is { } state ? Results.Ok(state) : Results.NotFound());

// Minimal stand-in for the Itinerary service: "tell me about disruptions to this flight".
flights.MapPost("/watch", async (WatchRequest request, FlightStateStore store, ICurrentUser user) =>
{
    var flightKey = $"{request.FlightNumber.ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal)}:{request.FlightDate:yyyy-MM-dd}";
    await store.WatchAsync(flightKey, user.Id);
    return Results.Accepted(value: new { flightKey });
});

// Ops/demo: inject a status change (used by the E2E test and live demos).
app.MapPost("/api/ops/disruptions/simulate", async (FlightStatusUpdate update, IMessageProducer producer, CancellationToken ct) =>
    {
        await FlightStatusSimulator.ProduceAsync(producer, update, ct);
        return Results.Accepted();
    })
    .RequireAuthorization(Policies.Ops)
    .WithTags("Ops");

await app.RunAsync();

internal sealed record WatchRequest(string FlightNumber, DateOnly FlightDate);
