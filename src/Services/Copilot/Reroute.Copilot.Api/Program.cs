using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Distributed;
using Qdrant.Client;
using Reroute.BuildingBlocks.AI;
using Reroute.BuildingBlocks.Observability;
using Reroute.BuildingBlocks.Web;
using Reroute.Copilot.Api.Rag;
using Reroute.Copilot.Api.Recommendations;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("copilot-api", AiServiceCollectionExtensions.TelemetrySource);
builder.AddJwtResourceServer();

builder.Services.AddRerouteAI(builder.Configuration);
builder.Services.AddSingleton(_ => new QdrantClient(
    builder.Configuration["Qdrant:Host"] ?? "localhost",
    builder.Configuration.GetValue("Qdrant:Port", 6334)));
builder.Services.AddSingleton<QdrantVectorStore>();
builder.Services.AddScoped<CorpusIngestion>();
builder.Services.AddScoped<RebookingRanker>();
builder.Services.AddHostedService<CorpusSeeder>();

var redis = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redis))
{
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redis); // semantic answer cache
}

builder.Services.AddScoped(sp => new RagPipeline(
    sp.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>(),
    sp.GetRequiredService<Microsoft.Extensions.AI.IChatClient>(),
    sp.GetRequiredService<QdrantVectorStore>(),
    sp.GetService<IDistributedCache>(),
    sp.GetRequiredService<ILogger<RagPipeline>>()));

// Service-to-service call with the USER's token (token relay) — Claims enforces its own authorisation.
builder.Services.AddHttpClient("claims", c => c.BaseAddress = new Uri(builder.Configuration["Services:Claims"] ?? "http://localhost:5104"));

var app = builder.Build();
app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapServiceDefaults();

app.MapPost("/api/copilot/ask", (AskRequest request, RagPipeline rag, IHttpClientFactory http, HttpContext ctx, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 1000)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["question"] = ["Ask a question of up to 1000 characters."] });
        }

        // Server-Sent Events: citations first, then answer deltas, then "done".
        return TypedResults.ServerSentEvents(Stream(request, rag, http, ctx, ct));
    })
    .RequireAuthorization(Policies.Traveller)
    .WithTags("Copilot");

app.MapPost("/api/recommendations/rank", async (RankRequest request, RebookingRanker ranker, CancellationToken ct) =>
        Results.Ok(await ranker.RankAsync(request, ct)))
    .RequireAuthorization(Policies.Traveller)
    .WithTags("Recommendations");

app.MapPost("/api/copilot/ingest", async (CorpusIngestion ingestion, IHostEnvironment env, CancellationToken ct) =>
        Results.Ok(new { chunks = await ingestion.IngestAsync(CorpusIngestion.LoadFromDirectory(Path.Combine(env.ContentRootPath, "Corpus")), ct) }))
    .RequireAuthorization(Policies.Ops)
    .WithTags("Ops");

await app.RunAsync();

static async IAsyncEnumerable<SseItem<object>> Stream(
    AskRequest request, RagPipeline rag, IHttpClientFactory http, HttpContext ctx, [EnumeratorCancellation] CancellationToken ct)
{
    var facts = request.ClaimId is { } claimId ? await LoadClaimFactsAsync(claimId, http, ctx, ct) : null;
    await foreach (var evt in rag.AskAsync(request.Question, facts, request.Airline, ct))
    {
        yield return evt switch
        {
            CitationsEvent c => new SseItem<object>(c.Citations, "citations"),
            DeltaEvent d => new SseItem<object>(d.Text, "delta"),
            _ => throw new InvalidOperationException(),
        };
    }

    yield return new SseItem<object>(string.Empty, "done");
}

static async Task<ClaimFacts?> LoadClaimFactsAsync(Guid claimId, IHttpClientFactory http, HttpContext ctx, CancellationToken ct)
{
    var client = http.CreateClient("claims");
    using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/claims/{claimId}");
    if (AuthenticationHeaderValue.TryParse(ctx.Request.Headers.Authorization, out var auth))
    {
        request.Headers.Authorization = auth;
    }

    using var response = await client.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
    {
        return null;
    }

    var claim = await response.Content.ReadFromJsonAsync<ClaimView>(ct);
    return claim is null
        ? null
        : new ClaimFacts(claim.FlightNumber, $"{claim.Origin}→{claim.Destination}", claim.Status, claim.CompensationAmount, claim.Currency, claim.EligibilityReason);
}

internal sealed record AskRequest(string Question, Guid? ClaimId, string? Airline);

internal sealed record ClaimView(string FlightNumber, string Origin, string Destination, string Status, decimal CompensationAmount, string Currency, string EligibilityReason);
