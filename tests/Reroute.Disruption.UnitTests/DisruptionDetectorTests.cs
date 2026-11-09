using Reroute.Contracts;
using Reroute.Disruption.Api.Detection;
using Shouldly;
using Xunit;

namespace Reroute.Disruption.UnitTests;

public sealed class DisruptionDetectorTests
{
    private static FlightStatusUpdate Update(string status, int delay = 0, string? reason = null) =>
        new("KL1234:2026-11-09", "KL1234", new DateOnly(2026, 11, 9), "AMS", "CDG", status, delay, reason, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(null, 0, FlightStatuses.Cancelled, 0, "Cancellation")]
    [InlineData(FlightStatuses.Cancelled, 0, FlightStatuses.Cancelled, 0, null)]      // already known → no duplicate alert
    [InlineData(FlightStatuses.Delayed, 30, FlightStatuses.Delayed, 59, null)]        // below 1 h
    [InlineData(FlightStatuses.Delayed, 30, FlightStatuses.Delayed, 60, "Delay")]     // crossed 1 h
    [InlineData(FlightStatuses.Delayed, 90, FlightStatuses.Delayed, 120, null)]       // still between thresholds
    [InlineData(FlightStatuses.Delayed, 150, FlightStatuses.Delayed, 180, "Delay")]   // crossed 3 h (compensation)
    [InlineData(FlightStatuses.Scheduled, 0, FlightStatuses.Diverted, 0, "Diversion")]
    public void Emits_only_on_meaningful_transitions(string? prevStatus, int prevDelay, string status, int delay, string? expected)
    {
        var previous = prevStatus is null ? null : new FlightState(prevStatus, prevDelay);

        DisruptionDetector.Detect(previous, Update(status, delay)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Severe weather at destination", true)]
    [InlineData("Air traffic control strike", true)]
    [InlineData("Crew shortage", false)]        // airline's own problem → compensation still due
    [InlineData("Airline staff strike", false)] // CJEU: own-staff strikes are not extraordinary
    [InlineData(null, false)]
    public void Flags_likely_extraordinary_circumstances(string? reason, bool expected) =>
        DisruptionDetector.LikelyExtraordinary(reason).ShouldBe(expected);
}
