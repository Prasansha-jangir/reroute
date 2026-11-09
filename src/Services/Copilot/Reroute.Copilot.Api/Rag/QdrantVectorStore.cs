using System.Security.Cryptography;
using System.Text;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace Reroute.Copilot.Api.Rag;

public sealed record RetrievedChunk(string Id, double Score, string Title, string Section, string Text, string Airline);

/// <summary>
/// Qdrant adapter. Point IDs are deterministic (hash of document/version/chunk) → re-ingestion is idempotent.
/// The collection name carries a version so switching embedding models is an alias swap, not a destructive migration.
/// </summary>
public sealed class QdrantVectorStore(QdrantClient client, Reroute.BuildingBlocks.AI.AiOptions ai)
{
    public const string Collection = "rights_corpus_v1";

    public async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (!await client.CollectionExistsAsync(Collection, ct))
        {
            await client.CreateCollectionAsync(
                Collection,
                new VectorParams { Size = (ulong)ai.EmbeddingDimensions, Distance = Distance.Cosine },
                quantizationConfig: new QuantizationConfig { Scalar = new ScalarQuantization { Type = QuantizationType.Int8 } },
                cancellationToken: ct);
            await client.CreatePayloadIndexAsync(Collection, "airline", PayloadSchemaType.Keyword, cancellationToken: ct);
        }
    }

    public async Task<ulong> CountAsync(CancellationToken ct) => await client.CountAsync(Collection, cancellationToken: ct);

    public async Task UpsertAsync(IReadOnlyList<Chunk> chunks, IReadOnlyList<ReadOnlyMemory<float>> vectors, CancellationToken ct)
    {
        var points = chunks.Select((c, i) => new PointStruct
        {
            Id = PointIdFor(c),
            Vectors = vectors[i].ToArray(),
            Payload =
            {
                ["documentId"] = c.DocumentId,
                ["title"] = c.DocumentTitle,
                ["section"] = c.Section,
                ["text"] = c.Text,
                ["airline"] = c.Airline,
                ["language"] = c.Language,
                ["version"] = c.Version,
            },
        }).ToList();

        await client.UpsertAsync(Collection, points, cancellationToken: ct);
    }

    /// <summary>Vector search restricted to generic ("*") documents plus the given airline's own conditions.</summary>
    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(ReadOnlyMemory<float> query, string? airline, int limit, CancellationToken ct)
    {
        var filter = airline is null
            ? MatchKeyword("airline", "*")
            : MatchKeyword("airline", "*") | MatchKeyword("airline", airline);

        var results = await client.QueryAsync(
            Collection,
            query: query.ToArray(),
            filter: filter,
            limit: (ulong)limit,
            payloadSelector: true,
            cancellationToken: ct);

        return results.Select(r => new RetrievedChunk(
            r.Id.Uuid,
            r.Score,
            r.Payload["title"].StringValue,
            r.Payload["section"].StringValue,
            r.Payload["text"].StringValue,
            r.Payload["airline"].StringValue)).ToList();
    }

    private static PointId PointIdFor(Chunk c)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{c.DocumentId}|{c.Version}|{c.Index}"));
        return new PointId { Uuid = new Guid(hash.AsSpan(0, 16)).ToString() };
    }
}
