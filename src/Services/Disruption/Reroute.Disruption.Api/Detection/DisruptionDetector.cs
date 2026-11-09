using Reroute.Contracts;

namespace Reroute.Disruption.Api.Detection;

public sealed record FlightState(string Status, int DelayMinutes);

/// <summary>
/// Pure state-transition logic: given the previous known state and a new observation, is this a disruption
/// travellers must hear about? Kept free of I/O so it is trivially unit-testable.
/// </summary>
public static class DisruptionDetector
{
    /// <summary>Notify early (1 h) even though EU261 compensation starts at 3 h: rebooking options vanish fast.</summary>
    public const int NotifyDelayMinutes = 60;
    public const int CompensationDelayMinutes = 180;

    public static string? Detect(FlightState? previous, FlightStatusUpdate current)
    {
        ArgumentNullException.ThrowIfNull(current);

        return current.Status switch
        {
            FlightStatuses.Cancelled when previous?.Status != FlightStatuses.Cancelled => "Cancellation",
            FlightStatuses.Diverted when previous?.Status != FlightStatuses.Diverted => "Diversion",
            FlightStatuses.Delayed when CrossedThreshold(previous?.DelayMinutes ?? 0, current.DelayMinutes) => "Delay",
            _ => null,
        };
    }

    /// <summary>
    /// Heuristic hint only (the Claims policy makes the legal call). Note: per CJEU case law an airline's
    /// own staff strike is NOT extraordinary, while ATC strikes and severe weather usually are.
    /// </summary>
    public static bool LikelyExtraordinary(string? reason) =>
        reason is not null &&
        (reason.Contains("weather", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("air traffic control", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("ATC", StringComparison.Ordinal) ||
         reason.Contains("security", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("bird strike", StringComparison.OrdinalIgnoreCase));

    // Emit once when crossing 1 h (heads-up) and once when crossing 3 h (compensation likely) — not on every update.
    private static bool CrossedThreshold(int previousDelay, int currentDelay) =>
        (previousDelay < NotifyDelayMinutes && currentDelay >= NotifyDelayMinutes) ||
        (previousDelay < CompensationDelayMinutes && currentDelay >= CompensationDelayMinutes);
}
