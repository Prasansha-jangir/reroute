using Microsoft.Extensions.AI;

namespace Reroute.Copilot.Api.Recommendations;

public sealed record RebookingOption(
    string Id,
    string Kind,              // Flight | Train | Bus
    string Carrier,
    DateTimeOffset ArrivesAt,
    int Stops,
    int MinConnectionMinutes,
    decimal Price,
    double CarrierOnTimeRate, // 0..1, from historical disruption data
    string Description);

public sealed record RankRequest(DateTimeOffset OriginalArrival, string? Preferences, IReadOnlyList<RebookingOption> Options);

public sealed record RankedOption(string Id, double Score, IReadOnlyList<string> Reasons);

/// <summary>
/// Hybrid recommender for rebooking alternatives:
///   score = w1·arrival + w2·preferenceSimilarity + w3·price + w4·reliability − w5·connectionRisk
/// Content-based personalisation via embeddings, explainable via per-factor reasons.
/// Weights are hand-tuned today; the roadmap learns them offline from accept/click logs exported from Kafka.
/// </summary>
public sealed class RebookingRanker(IEmbeddingGenerator<string, Embedding<float>> embeddings)
{
    internal static readonly (double Arrival, double Preference, double Price, double Reliability, double ConnectionRisk) Weights =
        (0.45, 0.20, 0.15, 0.10, 0.10);

    public async Task<IReadOnlyList<RankedOption>> RankAsync(RankRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Options.Count == 0)
        {
            return [];
        }

        var similarities = await PreferenceSimilaritiesAsync(request, ct);
        var maxPrice = request.Options.Max(o => o.Price);

        return request.Options
            .Select((option, i) => Score(option, request.OriginalArrival, similarities[i], maxPrice))
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    internal static RankedOption Score(RebookingOption o, DateTimeOffset originalArrival, double preferenceSimilarity, decimal maxPrice)
    {
        var lateHours = Math.Max(0, (o.ArrivesAt - originalArrival).TotalHours);
        var arrival = 1.0 / (1.0 + (lateHours / 3.0));                  // 1.0 on time, 0.5 three hours late
        var price = maxPrice == 0 ? 1.0 : 1.0 - (double)(o.Price / maxPrice);
        var connectionRisk = o.Stops == 0 ? 0 : Math.Clamp((90 - o.MinConnectionMinutes) / 90.0, 0, 1);

        var score = (Weights.Arrival * arrival) + (Weights.Preference * preferenceSimilarity) + (Weights.Price * price)
                    + (Weights.Reliability * o.CarrierOnTimeRate) - (Weights.ConnectionRisk * connectionRisk);

        var reasons = new List<string>();
        if (lateHours <= 2)
        {
            reasons.Add($"Arrives within {Math.Ceiling(lateHours)}h of your original plan");
        }

        if (o.Stops == 0)
        {
            reasons.Add("Direct");
        }
        else if (connectionRisk > 0.5)
        {
            reasons.Add($"Tight {o.MinConnectionMinutes}-min connection");
        }

        if (preferenceSimilarity > 0.5)
        {
            reasons.Add("Matches your preferences");
        }

        if (o.CarrierOnTimeRate >= 0.85)
        {
            reasons.Add($"{o.Carrier} is {o.CarrierOnTimeRate:P0} on time on this route");
        }

        return new RankedOption(o.Id, Math.Round(score, 4), reasons);
    }

    private async Task<double[]> PreferenceSimilaritiesAsync(RankRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Preferences))
        {
            return new double[request.Options.Count];
        }

        var inputs = request.Options.Select(o => $"{o.Kind} {o.Carrier} {(o.Stops == 0 ? "direct" : $"{o.Stops} stops")} {o.Description}")
            .Prepend(request.Preferences);
        var vectors = await embeddings.GenerateAsync(inputs, cancellationToken: ct);
        var preference = vectors[0].Vector;

        return vectors.Skip(1)
            .Select(v => Math.Max(0, Cosine(preference.Span, v.Vector.Span)))
            .ToArray();
    }

    private static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
