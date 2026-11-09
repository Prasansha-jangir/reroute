using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Hybrid;
using Reroute.BuildingBlocks.Messaging;
using Reroute.BuildingBlocks.Persistence;
using Reroute.Claims.Application.Abstractions;
using Reroute.Claims.Domain;
using Reroute.Claims.Infrastructure.EventStore;
using Reroute.Claims.Infrastructure.Projections;
using Reroute.Contracts;

namespace Reroute.Claims.Infrastructure.Persistence;

/// <summary>
/// Saves a Claim as ONE atomic unit of work:
///   1. append new events (optimistic concurrency),
///   2. update the read model (inline projection),
///   3. write integration events to the outbox,
/// then commit, and finally invalidate caches. No dual-write problem: Kafka is fed from the outbox.
/// </summary>
public sealed class EventSourcedClaimRepository(
    ISqlConnectionFactory connectionFactory,
    SqlEventStore eventStore,
    EventSerializer serializer,
    HybridCache cache,
    TimeProvider timeProvider) : IClaimRepository
{
    public const string Schema = "claims";

    public async Task<Claim?> GetAsync(ClaimId id, CancellationToken cancellationToken)
    {
        var stored = await eventStore.ReadStreamAsync(id.Value, cancellationToken);
        return stored.Count == 0 ? null : Claim.FromHistory(stored.Select(eventStore.ToDomainEvent));
    }

    public async Task SaveAsync(Claim claim, CancellationToken cancellationToken)
    {
        var pending = claim.UncommittedEvents.ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await eventStore.AppendAsync(connection, tx, claim.Id.Value, claim.Version, pending, now);

        var version = claim.Version;
        foreach (var domainEvent in pending)
        {
            version++;
            await ClaimSummaryProjection.ApplyAsync(connection, tx, domainEvent, version, now);

            var envelope = new ClaimEventEnvelope(
                claim.Id.Value, claim.TravellerId, EventSerializer.TypeName(domainEvent), version, serializer.Serialize(domainEvent), now);
            await SqlOutbox.EnqueueAsync(connection, tx, Schema, new OutboxMessage(
                Guid.CreateVersion7(),
                BuildingBlocks.Messaging.Topics.ClaimsEvents,
                claim.Id.ToString(),
                envelope.EventType,
                JsonSerializer.Serialize(envelope, EventSerializer.Options),
                TraceContext.Current,
                now));
        }

        await tx.CommitAsync(cancellationToken);
        claim.MarkCommitted();

        // Invalidate AFTER commit; tags fan out to every replica's L1 + the shared L2 (Redis).
        await cache.RemoveByTagAsync(CacheTags.Claim(claim.Id.Value), cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.Traveller(claim.TravellerId), cancellationToken);
    }
}

public static class CacheTags
{
    public static string Claim(Guid claimId) => $"claim:{claimId}";

    public static string Traveller(string travellerId) => $"traveller:{travellerId}";
}
