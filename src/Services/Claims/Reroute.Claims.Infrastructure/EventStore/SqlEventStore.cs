using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Reroute.BuildingBlocks.Messaging;
using Reroute.BuildingBlocks.Persistence;
using Reroute.Claims.Application.Abstractions;
using Reroute.SharedKernel;

namespace Reroute.Claims.Infrastructure.EventStore;

public sealed record StoredEvent(long GlobalPosition, Guid StreamId, int StreamVersion, string EventType, string Data, DateTimeOffset OccurredAt);

/// <summary>
/// Event store on plain SQL Server + Dapper. No framework: one table, one unique constraint,
/// and SQL Server's own transaction semantics provide atomic append + optimistic concurrency.
/// </summary>
public sealed class SqlEventStore(ISqlConnectionFactory connectionFactory, EventSerializer serializer)
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    /// <summary>Appends events at versions expectedVersion+1..n inside the caller's transaction.</summary>
    public async Task AppendAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid streamId,
        int expectedVersion,
        IReadOnlyList<IDomainEvent> events,
        DateTimeOffset occurredAt)
    {
        var metadata = JsonSerializer.Serialize(new { traceParent = TraceContext.Current });
        var rows = events.Select((e, i) => new
        {
            StreamId = streamId,
            StreamVersion = expectedVersion + i + 1,
            EventType = EventSerializer.TypeName(e),
            Data = serializer.Serialize(e),
            Metadata = metadata,
            OccurredAt = occurredAt,
        });

        try
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO claims.Events (StreamId, StreamVersion, EventType, Data, Metadata, OccurredAt)
                VALUES (@StreamId, @StreamVersion, @EventType, @Data, @Metadata, @OccurredAt)
                """,
                rows,
                transaction);
        }
        catch (SqlException ex) when (ex.Number is UniqueConstraintViolation or UniqueIndexViolation)
        {
            throw new ConcurrencyException(
                $"Stream {streamId} was modified concurrently (expected version {expectedVersion}).", ex);
        }
    }

    public async Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(Guid streamId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var events = await connection.QueryAsync<StoredEvent>(
            new CommandDefinition(
                """
                SELECT GlobalPosition, StreamId, StreamVersion, EventType, Data, OccurredAt
                FROM claims.Events
                WHERE StreamId = @streamId
                ORDER BY StreamVersion
                """,
                new { streamId },
                cancellationToken: cancellationToken));
        return events.AsList();
    }

    public IDomainEvent ToDomainEvent(StoredEvent stored) => serializer.Deserialize(stored.EventType, stored.Data);
}
