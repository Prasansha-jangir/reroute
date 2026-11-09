namespace Reroute.Claims.Domain;

public sealed record EligibilityDecision(bool IsEligible, Money Compensation, string Reason, double DistanceKm, bool ReducedForRerouting)
{
    public static EligibilityDecision Ineligible(string reason, double distanceKm) =>
        new(false, Money.Zero(), reason, distanceKm, false);
}

/// <summary>
/// Domain service implementing (a deliberately simplified model of) Regulation (EC) No 261/2004:
/// <list type="bullet">
/// <item>Scope (Art. 3): departs from the EU, or arrives in the EU on an EU carrier.</item>
/// <item>Amount (Art. 7(1)): €250 ≤ 1500 km; €400 intra-EU &gt; 1500 km or 1500–3500 km; €600 otherwise.</item>
/// <item>Delay: arrival delay ≥ 3 h is treated as cancellation (Sturgeon, C-402/07).</item>
/// <item>Cancellation (Art. 5): no compensation with ≥ 14 days' notice, or with an acceptable re-route.</item>
/// <item>Extraordinary circumstances (Art. 5(3)): exempt.</item>
/// <item>Reduction (Art. 7(2)): 50 % if re-routed arrival delay is within 2/3/4 h depending on distance band.</item>
/// </list>
/// Pure and deterministic: money is computed by code and only *explained* by the AI copilot.
/// </summary>
public static class Eu261CompensationPolicy
{
    public const int DelayThresholdMinutes = 180;
    public const int CancellationNoticeThresholdDays = 14;

    public static EligibilityDecision Assess(FlightDisruption disruption)
    {
        ArgumentNullException.ThrowIfNull(disruption);
        var distanceKm = Math.Round(GreatCircle.DistanceKm(disruption.Origin, disruption.Destination), 0);

        if (!IsInScope(disruption))
        {
            return EligibilityDecision.Ineligible("Flight is outside EU261 scope (non-EU departure on a non-EU carrier).", distanceKm);
        }

        if (disruption.ExtraordinaryCircumstances)
        {
            return EligibilityDecision.Ineligible("Disruption caused by extraordinary circumstances (Art. 5(3)).", distanceKm);
        }

        var band = Band(disruption, distanceKm);

        switch (disruption.Type)
        {
            case DisruptionType.Delay when disruption.ArrivalDelayMinutes < DelayThresholdMinutes:
                return EligibilityDecision.Ineligible(
                    $"Arrival delay of {disruption.ArrivalDelayMinutes} min is below the 3-hour threshold.", distanceKm);

            case DisruptionType.Delay:
                return new(true, band.Amount, $"Arrival delay of {disruption.ArrivalDelayMinutes} min ≥ 3 h.", distanceKm, false);

            case DisruptionType.DeniedBoarding:
                return new(true, band.Amount, "Denied boarding against the passenger's will (Art. 4).", distanceKm, false);

            case DisruptionType.Cancellation:
                return AssessCancellation(disruption, band, distanceKm);

            default:
                throw new ArgumentOutOfRangeException(nameof(disruption), disruption.Type, "Unknown disruption type.");
        }
    }

    private static EligibilityDecision AssessCancellation(FlightDisruption d, DistanceBand band, double distanceKm)
    {
        var notice = d.CancellationNoticeDays ?? 0;
        if (notice >= CancellationNoticeThresholdDays)
        {
            return EligibilityDecision.Ineligible($"Cancellation was notified {notice} days in advance (≥ 14).", distanceKm);
        }

        if (d.ReroutedArrivalDelayMinutes is { } rerouteDelay)
        {
            // Art. 5(1)(c)(ii)/(iii): an acceptable re-route removes the right to compensation.
            var acceptableRerouteDelay = notice >= 7 ? 240 : 120;
            if (rerouteDelay < acceptableRerouteDelay)
            {
                return EligibilityDecision.Ineligible(
                    $"Re-routed arrival only {rerouteDelay} min late with {notice} days' notice.", distanceKm);
            }

            // Art. 7(2): halve when the re-routed arrival is within the band's threshold.
            if (rerouteDelay <= band.ReductionThresholdMinutes)
            {
                return new(true, band.Amount.Half(),
                    $"Cancelled with {notice} days' notice; re-routed within {band.ReductionThresholdMinutes / 60} h, so compensation is reduced by 50% (Art. 7(2)).",
                    distanceKm, true);
            }
        }

        return new(true, band.Amount, $"Cancelled with {notice} days' notice (< 14).", distanceKm, false);
    }

    private static bool IsInScope(FlightDisruption d) =>
        d.Origin.IsInEu || (d.Destination.IsInEu && d.OperatedByEuCarrier);

    private static DistanceBand Band(FlightDisruption d, double distanceKm)
    {
        var intraEu = d.Origin.IsInEu && d.Destination.IsInEu;
        return distanceKm switch
        {
            <= 1500 => new(Money.Eur(250), 120),
            _ when intraEu => new(Money.Eur(400), 180),
            <= 3500 => new(Money.Eur(400), 180),
            _ => new(Money.Eur(600), 240),
        };
    }

    private sealed record DistanceBand(Money Amount, int ReductionThresholdMinutes);
}

/// <summary>Haversine great-circle distance (the method EU261 uses).</summary>
public static class GreatCircle
{
    private const double EarthRadiusKm = 6371.0;

    public static double DistanceKm(Airport from, Airport to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var dLat = ToRadians(to.Latitude - from.Latitude);
        var dLon = ToRadians(to.Longitude - from.Longitude);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) +
                (Math.Cos(ToRadians(from.Latitude)) * Math.Cos(ToRadians(to.Latitude)) *
                 Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
