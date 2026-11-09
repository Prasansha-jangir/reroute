using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Reroute.BuildingBlocks.Observability;

namespace Reroute.BuildingBlocks.Messaging;

public sealed record OutgoingMessage(
    string Topic,
    string Key,
    string Type,
    string Payload,
    Guid MessageId,
    string? TraceParent = null,
    IReadOnlyDictionary<string, string>? ExtraHeaders = null);

public interface IMessageProducer
{
    Task ProduceAsync(OutgoingMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Idempotent Kafka producer (enable.idempotence + acks=all): broker-side dedupe of retries, per-partition order kept.
/// W3C trace context travels in the "traceparent" header so traces survive the async hop.
/// </summary>
public sealed class KafkaMessageProducer : IMessageProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;

    public KafkaMessageProducer(IOptions<KafkaOptions> options)
    {
        var config = options.Value.Apply(new ProducerConfig
        {
            EnableIdempotence = true,
            Acks = Acks.All,
            LingerMs = 5,
            CompressionType = CompressionType.Lz4,
            MessageTimeoutMs = 30_000,
        });
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task ProduceAsync(OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        using var activity = RerouteTelemetry.ActivitySource.StartActivity(
            $"{message.Topic} publish",
            ActivityKind.Producer,
            parentContext: ActivityContext.TryParse(message.TraceParent, null, out var parent) ? parent : default);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", message.Topic);
        activity?.SetTag("messaging.message.id", message.MessageId.ToString());

        var headers = new Headers
        {
            { MessageHeaders.MessageId, Encoding.UTF8.GetBytes(message.MessageId.ToString()) },
            { MessageHeaders.MessageType, Encoding.UTF8.GetBytes(message.Type) },
        };
        if (activity?.Id is { } traceParent)
        {
            headers.Add(MessageHeaders.TraceParent, Encoding.UTF8.GetBytes(traceParent));
        }

        foreach (var (key, value) in message.ExtraHeaders ?? new Dictionary<string, string>())
        {
            headers.Add(key, Encoding.UTF8.GetBytes(value));
        }

        await _producer.ProduceAsync(
            message.Topic,
            new Message<string, string> { Key = message.Key, Value = message.Payload, Headers = headers },
            cancellationToken);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
