using Reroute.Claims.Domain;

namespace Reroute.Claims.UnitTests;

internal static class TestData
{
    public static readonly Airport Ams = new("AMS", "Amsterdam", 52.3105, 4.7683, true);
    public static readonly Airport Cdg = new("CDG", "Paris", 49.0097, 2.5479, true);
    public static readonly Airport Ath = new("ATH", "Athens", 37.9364, 23.9445, true);
    public static readonly Airport Run = new("RUN", "Réunion", -20.8871, 55.5103, true);
    public static readonly Airport Tlv = new("TLV", "Tel Aviv", 32.0055, 34.8854, false);
    public static readonly Airport Jfk = new("JFK", "New York", 40.6413, -73.7781, false);
    public static readonly Airport Lhr = new("LHR", "London", 51.4700, -0.4543, false);

    public static readonly IReadOnlyDictionary<string, Airport> Airports =
        new[] { Ams, Cdg, Ath, Run, Tlv, Jfk, Lhr }.ToDictionary(a => a.IataCode);

    public static FlightDisruption Disruption(
        Airport? origin = null,
        Airport? destination = null,
        DisruptionType type = DisruptionType.Delay,
        int arrivalDelay = 240,
        int? noticeDays = null,
        bool extraordinary = false,
        int? reroutedDelay = null,
        bool euCarrier = true) =>
        new(
            FlightNumber.Create("KL1234").Value,
            new DateOnly(2026, 10, 1),
            origin ?? Ams,
            destination ?? Cdg,
            type,
            arrivalDelay,
            noticeDays,
            extraordinary,
            reroutedDelay,
            euCarrier);
}
