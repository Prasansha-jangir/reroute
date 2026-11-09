using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reroute.BuildingBlocks.Observability;
using Reroute.BuildingBlocks.Persistence;

namespace Reroute.BuildingBlocks.Messaging;

public sealed record OutboxMessage(
    Guid Id,
    string Topic,
    string MessageKey,
    string Type,
    string Payload,
    string? TraceParent,
    DateTimeOffset OccurredAt);

public sealed partial class OutboxOptions
{
    public const string SectionName = "Outbox";

    private string _schema = "dbo";

    public bool Enabled { get; set; } = true;

    /// <summary>SQL schema that owns the OutboxMessages table (one per service).</summary>
    public string Schema
    {
        get => _schema;
        set => _schema = ValidIdentifier().IsMatch(value)
            ? value
            : throw new ArgumentException("Schema must be a simple SQL identifier.", nameof(value));
    }

    public int BatchSize { get; set; } = 100;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$")]
    private static partial Regex ValidIdentifier();
}

/// <summary>Writes outbox rows inside the caller's transaction — the heart of the transactional outbox pattern.</summary>
public static class SqlOutbox
{
    public static Task EnqueueAsync(IDbConnection connection, IDbTransaction transaction, string schema, OutboxMessage message) =>
        connection.ExecuteAsync(
            $"""
            INSERT INTO [{schema}].OutboxMessages (Id, Topic, MessageKey, Type, Payload, TraceParent, OccurredAt)
            VALUES (@Id, @Topic, @MessageKey, @Type, @Payload, @TraceParent, @OccurredAt)
            """,
            message,
            transaction);
}

/// <summary>
/// Ships committed outbox rows to Kafka. UPDLOCK + READPAST lets several replicas publish in parallel without
/// double-sending: each one skips rows another replica has locked. Delivery is at-least-once; consumers dedupe.
/// </summary>
public sealed class OutboxPublisher(
    ISqlConnectionFactory connectionFactory,
    IMessageProducer producer,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Outbox publisher disabled");
            return;
        }

        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await PublishBatchAsync(settings, stoppingToken);
                failures = 0;
                if (published < settings.BatchSize)
                {
                    await Task.Delay(settings.PollInterval, timeProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                var backoff = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, failures)));
                logger.LogError(ex, "Outbox publish failed (attempt {Failures}); backing off {Backoff}", failures, backoff);
                await Task.Delay(backoff, timeProvider, stoppingToken);
            }
        }
    }

    private async Task<int> PublishBatchAsync(OutboxOptions settings, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        var batch = (await connection.QueryAsync<OutboxRow>(
            $"""
            SELECT TOP (@BatchSize) Id, Topic, MessageKey, Type, Payload, TraceParent, OccurredAt
            FROM [{settings.Schema}].OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE ProcessedAt IS NULL
            ORDER BY OccurredAt
            """,
            new { settings.BatchSize },
            tx)).ToList();

        foreach (var row in batch)
        {
            await producer.ProduceAsync(
                new OutgoingMessage(row.Topic, row.MessageKey, row.Type, row.Payload, row.Id, row.TraceParent), ct);
            RerouteTelemetry.OutboxPublishLag.Record((timeProvider.GetUtcNow() - row.OccurredAt).TotalMilliseconds);
        }

        if (batch.Count > 0)
        {
            await connection.ExecuteAsync(
                $"UPDATE [{settings.Schema}].OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE Id IN @Ids",
                new { Ids = batch.Select(b => b.Id) },
                tx);
        }

        await tx.CommitAsync(ct);
        return batch.Count;
    }

    private sealed record OutboxRow(Guid Id, string Topic, string MessageKey, string Type, string Payload, string? TraceParent, DateTimeOffset OccurredAt);
}

/// <summary>Context propagation helper for writing outbox rows.</summary>
public static class TraceContext
{
    public static string? Current => Activity.Current?.Id;
}
