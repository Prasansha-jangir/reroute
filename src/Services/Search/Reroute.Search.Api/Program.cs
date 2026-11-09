using Reroute.BuildingBlocks.AI;
using Reroute.BuildingBlocks.Observability;
using Reroute.BuildingBlocks.Web;
using Reroute.Search.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("search-api", AiServiceCollectionExtensions.TelemetrySource);
builder.AddJwtResourceServer();
builder.Services.AddRerouteAI(builder.Configuration);
builder.Services.AddHttpClient<HotelSearch>(c => c.BaseAddress = new Uri(builder.Configuration["Elasticsearch:Url"] ?? "http://localhost:9200"));
builder.Services.AddHostedService<SearchIndexBootstrapper>();

var app = builder.Build();
app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapServiceDefaults();

var search = app.MapGroup("/api/search").RequireAuthorization(Policies.Traveller).WithTags("Search");

search.MapGet("/hotels", async (string airport, double? radiusKm, string? q, int? size, HotelSearch hotels, CancellationToken ct) =>
    HotelSearch.Airports.ContainsKey(airport)
        ? Results.Ok(await hotels.SearchAsync(airport, Math.Clamp(radiusKm ?? 15, 1, 50), q, Math.Clamp(size ?? 10, 1, 50), ct))
        : Results.ValidationProblem(new Dictionary<string, string[]> { ["airport"] = ["Unsupported airport."] }));

search.MapGet("/airports/suggest", async (string prefix, HotelSearch hotels, CancellationToken ct) =>
    Results.Ok(await hotels.SuggestAirportsAsync(prefix, ct)));

await app.RunAsync();

/// <summary>Creates the versioned index + alias and seeds demo hotels on first start.</summary>
internal sealed class SearchIndexBootstrapper(IServiceScopeFactory scopes, ILogger<SearchIndexBootstrapper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<HotelSearch>().EnsureIndexAsync(SeedHotels.All, stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Search index bootstrap skipped (is Elasticsearch running?)");
        }
    }
}

internal static class SeedHotels
{
    public static readonly Hotel[] All =
    [
        new("ams-1", "Runway Rest Schiphol", "Quiet soundproofed rooms inside the terminal, perfect for a short sleep between flights", ["24h reception", "gym", "late check-in"], 189, 52.3090, 4.7620, "AMS"),
        new("ams-2", "Hoofddorp Family Inn", "Spacious family rooms with free shuttle to the airport every 15 minutes", ["shuttle", "family rooms", "breakfast"], 119, 52.3030, 4.6890, "AMS"),
        new("ams-3", "Canal Budget Hostel", "Lively hostel in central Amsterdam, 20 minutes by train from the airport", ["bar", "shared kitchen"], 59, 52.3740, 4.8910, "AMS"),
        new("ams-4", "Badhoevedorp Business Hotel", "Business hotel with meeting rooms, fast wifi and a 24-hour gym", ["gym", "wifi", "meeting rooms", "shuttle"], 149, 52.3380, 4.7860, "AMS"),
        new("ams-5", "Polder Wellness Resort", "Calm countryside spa hotel with sauna and pool, ideal to recover after a cancelled flight", ["spa", "pool", "sauna", "shuttle"], 210, 52.2700, 4.7300, "AMS"),
        new("cdg-1", "Roissy Transit Hotel", "Simple rooms connected to Terminal 2 by walkway, open around the clock", ["24h reception", "late check-in"], 139, 49.0040, 2.5710, "CDG"),
        new("cdg-2", "Paris Nord Suites", "Apartment-style suites with kitchenette and free shuttle", ["shuttle", "kitchenette", "family rooms"], 129, 49.0150, 2.5250, "CDG"),
        new("fra-1", "Frankfurt Airport Tower Hotel", "Directly above the long-distance train station, quiet rooms and a fitness centre", ["gym", "train station", "24h reception"], 179, 50.0510, 8.5700, "FRA"),
    ];
}
