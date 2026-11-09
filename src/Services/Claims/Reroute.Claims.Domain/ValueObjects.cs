using System.Text.RegularExpressions;
using Reroute.SharedKernel;

namespace Reroute.Claims.Domain;

/// <summary>Strongly-typed ID: the compiler stops you passing a TripId where a ClaimId is expected.</summary>
public readonly record struct ClaimId(Guid Value)
{
    /// <summary>UUIDv7 is time-ordered → no index fragmentation on the clustered key.</summary>
    public static ClaimId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>IATA flight designator, e.g. KL1234 (carrier + 1–4 digits + optional suffix).</summary>
public sealed partial record FlightNumber
{
    private FlightNumber(string carrier, string number)
    {
        Carrier = carrier;
        Number = number;
    }

    public string Carrier { get; }

    public string Number { get; }

    public static Result<FlightNumber> Create(string? raw)
    {
        var normalised = (raw ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var match = Pattern().Match(normalised);
        return match.Success
            ? new FlightNumber(match.Groups["carrier"].Value, match.Groups["number"].Value)
            : Error.Validation(new Dictionary<string, string[]>
            {
                ["flightNumber"] = [$"'{raw}' is not a valid flight number (e.g. KL1234)."],
            });
    }

    public override string ToString() => Carrier + Number;

    [GeneratedRegex("^(?<carrier>[A-Z0-9]{2})(?<number>[0-9]{1,4}[A-Z]?)$")]
    private static partial Regex Pattern();
}

public sealed record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency = "EUR") => new(0m, currency);

    public static Money Eur(decimal amount) => new(amount, "EUR");

    public Money Half() => this with { Amount = decimal.Round(Amount / 2, 2, MidpointRounding.ToEven) };
}

/// <summary>Airport with the facts EU261 needs: location (distance) and whether it lies in the EU (scope).</summary>
public sealed record Airport(string IataCode, string Name, double Latitude, double Longitude, bool IsInEu);

public enum DisruptionType
{
    Delay,
    Cancellation,
    DeniedBoarding,
}

public enum ClaimStatus
{
    Filed,
    Eligible,
    Ineligible,
    SubmittedToAirline,
    AcceptedByAirline,
    RejectedByAirline,
    Escalated,
    Paid,
}

/// <summary>The facts of what happened to a flight, as relevant to EU261.</summary>
public sealed record FlightDisruption(
    FlightNumber Flight,
    DateOnly FlightDate,
    Airport Origin,
    Airport Destination,
    DisruptionType Type,
    int ArrivalDelayMinutes,
    int? CancellationNoticeDays,
    bool ExtraordinaryCircumstances,
    int? ReroutedArrivalDelayMinutes,
    bool OperatedByEuCarrier);
