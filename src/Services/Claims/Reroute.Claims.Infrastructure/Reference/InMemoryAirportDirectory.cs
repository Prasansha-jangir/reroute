using System.Collections.Frozen;
using Reroute.Claims.Application.Abstractions;
using Reroute.Claims.Domain;

namespace Reroute.Claims.Infrastructure.Reference;

/// <summary>
/// Reference data. A FrozenDictionary is ideal for read-only lookups on the hot path.
/// In production this would be loaded from an airports dataset (e.g. OurAirports) at startup.
/// </summary>
public sealed class InMemoryAirportDirectory : IAirportDirectory
{
    private static readonly FrozenDictionary<string, Airport> Airports = new Airport[]
    {
        new("AMS", "Amsterdam Schiphol", 52.3105, 4.7683, true),
        new("EIN", "Eindhoven", 51.4500, 5.3745, true),
        new("RTM", "Rotterdam The Hague", 51.9569, 4.4372, true),
        new("CDG", "Paris Charles de Gaulle", 49.0097, 2.5479, true),
        new("FRA", "Frankfurt", 50.0379, 8.5622, true),
        new("MUC", "Munich", 48.3538, 11.7861, true),
        new("MAD", "Madrid Barajas", 40.4983, -3.5676, true),
        new("BCN", "Barcelona El Prat", 41.2974, 2.0833, true),
        new("FCO", "Rome Fiumicino", 41.8003, 12.2389, true),
        new("LIS", "Lisbon", 38.7742, -9.1342, true),
        new("DUB", "Dublin", 53.4264, -6.2499, true),
        new("CPH", "Copenhagen", 55.6180, 12.6508, true),
        new("ATH", "Athens", 37.9364, 23.9445, true),
        new("HEL", "Helsinki", 60.3172, 24.9633, true),
        new("VIE", "Vienna", 48.1103, 16.5697, true),
        new("WAW", "Warsaw Chopin", 52.1657, 20.9671, true),
        new("PRG", "Prague", 50.1008, 14.2600, true),
        new("BRU", "Brussels", 50.9010, 4.4844, true),
        new("LPA", "Gran Canaria", 27.9319, -15.3866, true),
        new("RUN", "Réunion Roland Garros", -20.8871, 55.5103, true), // EU outermost region
        new("LHR", "London Heathrow", 51.4700, -0.4543, false),
        new("IST", "Istanbul", 41.2753, 28.7519, false),
        new("TLV", "Tel Aviv Ben Gurion", 32.0055, 34.8854, false),
        new("JFK", "New York JFK", 40.6413, -73.7781, false),
        new("ORD", "Chicago O'Hare", 41.9742, -87.9073, false),
        new("DXB", "Dubai", 25.2532, 55.3657, false),
        new("SIN", "Singapore Changi", 1.3644, 103.9915, false),
    }.ToFrozenDictionary(a => a.IataCode, StringComparer.OrdinalIgnoreCase);

    public Airport? Find(string iataCode) => Airports.GetValueOrDefault(iataCode);
}
