using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Reroute.BuildingBlocks.Messaging;
using Reroute.Contracts;

namespace Reroute.Claims.Infrastructure.Messaging;

/// <summary>
/// Consumes disruption.detected.v1 and caches the disruption facts per flight, so the UI can pre-fill
/// the "file a claim" form (GET /api/claims/prefill) without the Claims service calling Disruption synchronously.
/// </summary>
public sealed class DisruptionDetectedHandler(HybridCache cache, ILogger<DisruptionDetectedHandler> logger)
    : IMessageHandler<DisruptionDetected>
{
    public static string CacheKey(string flightKey) => $"reroute:claims:disruption:{flightKey}:v1";

    public async Task HandleAsync(DisruptionDetected message, MessageContext context, CancellationToken cancellationToken)
    {
        await cache.SetAsync(
            CacheKey(message.FlightKey),
            message,
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromDays(30) },
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Cached disruption {Type} for {FlightKey} affecting {Count} travellers",
            message.Type, message.FlightKey, message.AffectedTravellerIds.Count);
    }
}
