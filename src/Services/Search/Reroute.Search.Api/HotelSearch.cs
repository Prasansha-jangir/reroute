using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Reroute.BuildingBlocks.AI;

namespace Reroute.Search.Api;

public sealed record Hotel(string Id, string Name, string Description, string[] Amenities, decimal PricePerNight, double Latitude, double Longitude, string NearestAirport);

public sealed record HotelHit(Hotel Hotel, double Score, double DistanceKm);

/// <summary>
/// Elasticsearch through its REST API (query DSL kept visible on purpose — it's the thing worth learning).
/// Hybrid retrieval: (1) BM25 + geo filter + distance decay, (2) kNN over description embeddings,
/// fused client-side with Reciprocal Rank Fusion: score = Σ 1 / (60 + rank).
/// </summary>
public sealed class HotelSearch(HttpClient http, IEmbeddingGenerator<string, Embedding<float>> embeddings, AiOptions ai)
{
    public const string Alias = "hotels";
    public const string Index = "hotels_v1"; // alias → versioned index = zero-downtime reindex
    private const int RrfK = 60;

    public static readonly IReadOnlyDictionary<string, (double Lat, double Lon)> Airports = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
    {
        ["AMS"] = (52.3105, 4.7683), ["CDG"] = (49.0097, 2.5479), ["FRA"] = (50.0379, 8.5622), ["MAD"] = (40.4983, -3.5676),
    };

    public async Task<IReadOnlyList<HotelHit>> SearchAsync(string airport, double radiusKm, string? text, int size, CancellationToken ct)
    {
        var (lat, lon) = Airports[airport];
        var geoFilter = new JsonObject
        {
            ["geo_distance"] = new JsonObject { ["distance"] = $"{radiusKm}km", ["location"] = new JsonObject { ["lat"] = lat, ["lon"] = lon } },
        };

        // 1) Lexical + geo: BM25 on name/description/amenities, boosted by proximity (gauss decay).
        var lexical = new JsonObject
        {
            ["size"] = size * 2,
            ["query"] = new JsonObject
            {
                ["function_score"] = new JsonObject
                {
                    ["query"] = new JsonObject
                    {
                        ["bool"] = new JsonObject
                        {
                            ["filter"] = new JsonArray(geoFilter),
                            ["should"] = string.IsNullOrWhiteSpace(text)
                                ? new JsonArray()
                                : new JsonArray(new JsonObject
                                {
                                    ["multi_match"] = new JsonObject { ["query"] = text, ["fields"] = new JsonArray("name^2", "description", "amenities^1.5") },
                                }),
                        },
                    },
                    ["functions"] = new JsonArray(new JsonObject
                    {
                        ["gauss"] = new JsonObject { ["location"] = new JsonObject { ["origin"] = new JsonObject { ["lat"] = lat, ["lon"] = lon }, ["scale"] = "5km" } },
                    }),
                    ["boost_mode"] = "multiply",
                },
            },
        };

        var rankings = new List<List<Hotel>> { await QueryAsync(lexical, ct) };

        // 2) Semantic: kNN on embeddings, same geo filter (filtered HNSW search).
        if (!string.IsNullOrWhiteSpace(text))
        {
            var vector = (await embeddings.GenerateAsync([text], cancellationToken: ct))[0].Vector.ToArray();
            var knn = new JsonObject
            {
                ["size"] = size * 2,
                ["knn"] = new JsonObject
                {
                    ["field"] = "descriptionVector",
                    ["query_vector"] = new JsonArray(vector.Select(v => (JsonNode)v).ToArray()),
                    ["k"] = size * 2,
                    ["num_candidates"] = 100,
                    ["filter"] = geoFilter.DeepClone(),
                },
            };
            rankings.Add(await QueryAsync(knn, ct));
        }

        return ReciprocalRankFusion(rankings)
            .Take(size)
            .Select(x => new HotelHit(x.Hotel, Math.Round(x.Score, 5), Math.Round(Haversine(lat, lon, x.Hotel.Latitude, x.Hotel.Longitude), 1)))
            .ToList();
    }

    internal static IEnumerable<(Hotel Hotel, double Score)> ReciprocalRankFusion(IEnumerable<List<Hotel>> rankings)
    {
        var scores = new Dictionary<string, (Hotel Hotel, double Score)>(StringComparer.Ordinal);
        foreach (var ranking in rankings)
        {
            for (var rank = 0; rank < ranking.Count; rank++)
            {
                var hotel = ranking[rank];
                var current = scores.TryGetValue(hotel.Id, out var s) ? s.Score : 0;
                scores[hotel.Id] = (hotel, current + (1.0 / (RrfK + rank + 1)));
            }
        }

        return scores.Values.OrderByDescending(s => s.Score);
    }

    public async Task<IReadOnlyList<string>> SuggestAirportsAsync(string prefix, CancellationToken ct)
    {
        await Task.CompletedTask;
        return Airports.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task EnsureIndexAsync(IEnumerable<Hotel> seed, CancellationToken ct)
    {
        using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/{Index}"), ct);
        if (head.IsSuccessStatusCode)
        {
            return;
        }

        var mapping = new JsonObject
        {
            ["settings"] = new JsonObject { ["number_of_shards"] = 1, ["number_of_replicas"] = 0 },
            ["mappings"] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["name"] = new JsonObject { ["type"] = "text" },
                    ["description"] = new JsonObject { ["type"] = "text", ["analyzer"] = "english" },
                    ["amenities"] = new JsonObject { ["type"] = "text", ["fields"] = new JsonObject { ["keyword"] = new JsonObject { ["type"] = "keyword" } } },
                    ["pricePerNight"] = new JsonObject { ["type"] = "scaled_float", ["scaling_factor"] = 100 },
                    ["location"] = new JsonObject { ["type"] = "geo_point" },
                    ["nearestAirport"] = new JsonObject { ["type"] = "keyword" },
                    ["descriptionVector"] = new JsonObject { ["type"] = "dense_vector", ["dims"] = ai.EmbeddingDimensions, ["index"] = true, ["similarity"] = "cosine" },
                },
            },
            ["aliases"] = new JsonObject { [Alias] = new JsonObject() },
        };
        (await http.PutAsJsonAsync($"/{Index}", mapping, ct)).EnsureSuccessStatusCode();

        var hotels = seed.ToList();
        var vectors = await embeddings.GenerateAsync(hotels.Select(h => $"{h.Name}. {h.Description}. {string.Join(", ", h.Amenities)}"), cancellationToken: ct);
        var bulk = new System.Text.StringBuilder();
        for (var i = 0; i < hotels.Count; i++)
        {
            var h = hotels[i];
            bulk.AppendLine(JsonSerializer.Serialize(new { index = new { _index = Index, _id = h.Id } }));
            bulk.AppendLine(JsonSerializer.Serialize(new
            {
                id = h.Id, name = h.Name, description = h.Description, amenities = h.Amenities, pricePerNight = h.PricePerNight,
                location = new { lat = h.Latitude, lon = h.Longitude }, nearestAirport = h.NearestAirport, descriptionVector = vectors[i].Vector.ToArray(),
            }));
        }

        using var content = new StringContent(bulk.ToString(), System.Text.Encoding.UTF8, "application/x-ndjson");
        (await http.PostAsync("/_bulk?refresh=true", content, ct)).EnsureSuccessStatusCode();
    }

    private async Task<List<Hotel>> QueryAsync(JsonObject body, CancellationToken ct)
    {
        body["_source"] = new JsonObject { ["excludes"] = new JsonArray("descriptionVector") };
        using var response = await http.PostAsJsonAsync($"/{Alias}/_search", body, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        return json!["hits"]!["hits"]!.AsArray().Select(hit =>
        {
            var s = hit!["_source"]!;
            return new Hotel(
                s["id"]!.GetValue<string>(), s["name"]!.GetValue<string>(), s["description"]!.GetValue<string>(),
                s["amenities"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray(), s["pricePerNight"]!.GetValue<decimal>(),
                s["location"]!["lat"]!.GetValue<double>(), s["location"]!["lon"]!.GetValue<double>(), s["nearestAirport"]!.GetValue<string>());
        }).ToList();
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        static double R(double d) => d * Math.PI / 180;
        var a = Math.Pow(Math.Sin(R(lat2 - lat1) / 2), 2) + (Math.Cos(R(lat1)) * Math.Cos(R(lat2)) * Math.Pow(Math.Sin(R(lon2 - lon1) / 2), 2));
        return 6371 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
