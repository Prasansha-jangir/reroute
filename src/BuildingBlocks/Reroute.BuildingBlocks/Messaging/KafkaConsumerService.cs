using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reroute.BuildingBlocks.Observability;

namespace Reroute.BuildingBlocks.Messaging;

public sealed record MessageContext(Guid MessageId, string Topic, string Key, string Type, int Partition, long Offset);

public interface IMessageHandler<in TMessage>
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken);
}

/// <summary>
/// At-least-once Kafka consumer with effectively-once processing:
/// manual offset commit after handling, inbox de-duplication, bounded retries, then dead-letter topic.
/// One scope per message → handlers can use scoped services (DB connections, caches).
/// </summary>
public sealed class KafkaConsumerService<TMessage, THandler>(
    IServiceScopeFactory scopeFactory,
    IMessageProducer producer,
    IOptions<KafkaOptions> kafkaOptions,
    KafkaConsumerService<TMessage, THandler>.Settings settings,
    ILogger<KafkaConsumerService<TMessage, THandler>> logger) : BackgroundService
    where THandler : IMessageHandler<TMessage>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record Settings(string Topic, string GroupId, int MaxAttempts = 3);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // Consume() is blocking — run the poll loop on a dedicated long-running thread.
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var config = kafkaOptions.Value.Apply(new ConsumerConfig
        {
            GroupId = settings.GroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
        });

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(settings.Topic);
        logger.LogInformation("Consuming {Topic} as {Group}", settings.Topic, settings.GroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning(ex, "Consume error on {Topic}", settings.Topic);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                if (result?.Message is null)
                {
                    continue;
                }

                await ProcessAsync(result, stoppingToken);
                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
        finally
        {
            consumer.Close(); // leave the group cleanly → fast rebalance
        }
    }

    private async Task ProcessAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        var headers = result.Message.Headers;
        var messageId = Guid.TryParse(Header(headers, MessageHeaders.MessageId), out var id) ? id : Guid.Empty;
        var type = Header(headers, MessageHeaders.MessageType) ?? typeof(TMessage).Name;
        ActivityContext.TryParse(Header(headers, MessageHeaders.TraceParent), null, out var parent);

        using var activity = RerouteTelemetry.ActivitySource.StartActivity($"{settings.Topic} process", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.consumer.group.name", settings.GroupId);
        activity?.SetTag("messaging.kafka.offset", result.Offset.Value);

        var context = new MessageContext(messageId, settings.Topic, result.Message.Key, type, result.Partition.Value, result.Offset.Value);

        TMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<TMessage>(result.Message.Value, Json);
            if (message is null)
            {
                throw new JsonException("Message body was null.");
            }
        }
        catch (JsonException ex)
        {
            await DeadLetterAsync(result, ex, ct); // poison message: retrying will never help
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var inbox = scope.ServiceProvider.GetService<IInbox>();
                if (inbox is not null && messageId != Guid.Empty && await inbox.HasProcessedAsync(messageId, settings.GroupId, ct))
                {
                    RerouteTelemetry.MessagesConsumed.Add(1, new("topic", settings.Topic), new("outcome", "duplicate"));
                    return;
                }

                await scope.ServiceProvider.GetRequiredService<THandler>().HandleAsync(message, context, ct);

                if (inbox is not null && messageId != Guid.Empty)
                {
                    await inbox.MarkProcessedAsync(messageId, settings.GroupId, ct);
                }

                RerouteTelemetry.MessagesConsumed.Add(1, new("topic", settings.Topic), new("outcome", "success"));
                return;
            }
            catch (Exception ex) when (attempt < settings.MaxAttempts && !ct.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMilliseconds((200 * Math.Pow(2, attempt)) + Random.Shared.Next(0, 100));
                logger.LogWarning(ex, "Attempt {Attempt} failed for {MessageId}; retrying in {Delay}", attempt, messageId, delay);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                await DeadLetterAsync(result, ex, ct);
                return;
            }
        }
    }

    private async Task DeadLetterAsync(ConsumeResult<string, string> result, Exception ex, CancellationToken ct)
    {
        logger.LogError(ex, "Dead-lettering message at {Topic}/{Partition}@{Offset}", settings.Topic, result.Partition.Value, result.Offset.Value);
        RerouteTelemetry.MessagesConsumed.Add(1, new("topic", settings.Topic), new("outcome", "dead_lettered"));
        var headers = result.Message.Headers;
        await producer.ProduceAsync(
            new OutgoingMessage(
                Topics.DeadLetter(settings.Topic),
                result.Message.Key,
                Header(headers, MessageHeaders.MessageType) ?? "unknown",
                result.Message.Value,
                Guid.TryParse(Header(headers, MessageHeaders.MessageId), out var id) ? id : Guid.NewGuid(),
                ExtraHeaders: new Dictionary<string, string>
                {
                    [MessageHeaders.Error] = ex.GetType().Name + ": " + ex.Message,
                    [MessageHeaders.OriginalTopic] = settings.Topic,
                }),
            ct);
    }

    private static string? Header(Headers headers, string name) =>
        headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
}
