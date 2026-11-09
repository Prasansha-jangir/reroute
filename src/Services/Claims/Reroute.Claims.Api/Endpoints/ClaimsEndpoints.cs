using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;
using Reroute.BuildingBlocks.Cqrs;
using Reroute.BuildingBlocks.Web;
using Reroute.Claims.Application.Claims;
using Reroute.Claims.Domain;
using Reroute.Claims.Infrastructure.Messaging;
using Reroute.Contracts;

namespace Reroute.Claims.Api.Endpoints;

/// <summary>
/// Thin HTTP adapter: translate HTTP → command/query, dispatch, translate Result → HTTP. No business logic here.
/// </summary>
public static class ClaimsEndpoints
{
    public static IEndpointRouteBuilder MapClaimsEndpoints(this IEndpointRouteBuilder app)
    {
        var claims = app.MapGroup("/api/claims")
            .WithTags("Claims")
            .RequireAuthorization(Policies.Traveller);

        claims.MapPost("/", async (FileClaimCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(command, ct)).ToHttp(r => Results.Created($"/api/claims/{r.ClaimId}", r)))
            .WithIdempotency()
            .WithName("FileClaim")
            .WithSummary("File an EU261 compensation claim; eligibility is assessed immediately.")
            .Produces<FileClaimResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        claims.MapGet("/", async ([FromQuery] ClaimStatus? status, [FromQuery] int? page, [FromQuery] int? pageSize, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Query(new ListMyClaimsQuery(status, page ?? 1, pageSize ?? 20), ct)))
            .WithName("ListMyClaims");

        claims.MapGet("/{claimId:guid}", async (Guid claimId, IDispatcher dispatcher, HttpContext http, CancellationToken ct) =>
                (await dispatcher.Query(new GetClaimQuery(claimId), ct)).ToHttp(dto =>
                {
                    http.Response.Headers.ETag = $"\"{dto.Version}\""; // stream version → HTTP optimistic concurrency
                    return Results.Ok(dto);
                }))
            .WithName("GetClaim");

        claims.MapGet("/{claimId:guid}/timeline", async (Guid claimId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Query(new GetClaimTimelineQuery(claimId), ct)).ToHttp(Results.Ok))
            .WithName("GetClaimTimeline")
            .WithSummary("Audit trail: every event in the claim's stream.");

        claims.MapPost("/{claimId:guid}/submit", async (Guid claimId, [FromHeader(Name = "If-Match")] string? ifMatch, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(new SubmitClaimToAirlineCommand(claimId, ParseETag(ifMatch)), ct)).ToHttp())
            .WithName("SubmitClaim");

        claims.MapGet("/prefill", async ([FromQuery] string flightKey, HybridCache cache, CancellationToken ct) =>
            {
                var disruption = await cache.GetOrCreateAsync<DisruptionDetected?>(
                    DisruptionDetectedHandler.CacheKey(flightKey), _ => ValueTask.FromResult<DisruptionDetected?>(null), cancellationToken: ct);
                return disruption is null ? Results.NotFound() : Results.Ok(disruption);
            })
            .WithName("PrefillFromDisruption");

        app.MapPost("/api/ops/claims/{claimId:guid}/airline-decision", async (Guid claimId, AirlineDecisionRequest body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(new RecordAirlineDecisionCommand(claimId, body.Accepted, body.AirlineReference, body.Reason), ct)).ToHttp())
            .WithTags("Ops")
            .RequireAuthorization(Policies.Ops);

        return app;
    }

    private static int? ParseETag(string? ifMatch) =>
        int.TryParse(ifMatch?.Trim('"', ' ', 'W', '/'), out var v) ? v : null;

    public sealed record AirlineDecisionRequest(bool Accepted, string? AirlineReference, string? Reason);
}
