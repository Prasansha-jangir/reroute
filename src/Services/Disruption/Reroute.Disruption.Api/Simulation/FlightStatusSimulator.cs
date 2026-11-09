using System.Text.Json;
using Microsoft.Extensions.Options;
using Reroute.BuildingBlocks.Messaging;
using Reroute.Contracts;

namespace Reroute.Disruption.Api.Simulation;

public sealed class SimulatorOptions
{
    public bool Enabled { get; set; }

    public int EventsPerSecond { get; set; } = 5;

    public int FlightCount { get; set; } = 200;
}

/// <summary>
/// Dev/demo traffic generator that stands in for a commercial flight-status feed. Doubles as a load generator:
/// set EventsPerSecond=2000 and watch consumer lag and detection latency in Grafana.
/// </summary>
public sealed class FlightStatusSimulator(
    IMessageProducer producer,
    IOptions<SimulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<FlightStatusSimulator> logger) : BackgroundService
{
    private static readonly string[] Carriers = ["KL", "HV", "LH", "AF", "IB", "SK", "TP", "FR", "U2"];
    private static readonly string[] Airports = ["AMS", "CDG", "FRA", "MAD", "BCN", "FCO", "LIS", "DUB", "CPH", "ATH", "JFK", "LHR"];
    private static readonly string?[] Reasons = [null, "Late inbound aircraft", "Technical issue", "Severe weather", "Air traffic control strike", "Crew shortage"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        logger.LogInformation("Flight simulator producing {Rate} events/s over {Flights} flights", settings.EventsPerSecond, settings.FlightCount);
        var random = new Random(42);
        var delays = new int[settings.FlightCount];
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / Math.Max(1, settings.EventsPerSecond)), timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var i = random.Next(settings.FlightCount);
            delays[i] = Math.Max(0, delays[i] + random.Next(-10, 40));
            var cancelled = random.NextDouble() < 0.002;
            var update = Build(i, cancelled ? FlightStatuses.Cancelled : delays[i] > 15 ? FlightStatuses.Delayed : FlightStatuses.Scheduled, delays[i], Reasons[random.Next(Reasons.Length)]);
            await ProduceAsync(producer, update, stoppingToken);
        }
    }

    public static Task ProduceAsync(IMessageProducer producer, FlightStatusUpdate update, CancellationToken ct) =>
        producer.ProduceAsync(
            new OutgoingMessage(
                Topics.FlightStatusRaw,
                update.FlightKey,
                nameof(FlightStatusUpdate),
                JsonSerializer.Serialize(update, JsonSerializerOptions.Web),
                Guid.CreateVersion7()),
            ct);

    private FlightStatusUpdate Build(int index, string status, int delay, string? reason)
    {
        var carrier = Carriers[index % Carriers.Length];
        var number = $"{carrier}{1000 + index}";
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return new FlightStatusUpdate(
            $"{number}:{today:yyyy-MM-dd}",
            number,
            today,
            Airports[index % Airports.Length],
            Airports[(index + 3) % Airports.Length],
            status,
            delay,
            status == FlightStatuses.Scheduled ? null : reason,
            timeProvider.GetUtcNow());
    }
}
