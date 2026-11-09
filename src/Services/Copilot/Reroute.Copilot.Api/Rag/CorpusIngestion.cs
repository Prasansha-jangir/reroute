using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Reroute.Copilot.Api.Rag;

/// <summary>Chunk → embed (batched) → upsert. Idempotent thanks to deterministic point IDs.</summary>
public sealed partial class CorpusIngestion(
    QdrantVectorStore store,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILogger<CorpusIngestion> logger)
{
    private const int BatchSize = 64;

    public async Task<int> IngestAsync(IEnumerable<SourceDocument> documents, CancellationToken ct)
    {
        await store.EnsureCollectionAsync(ct);
        var total = 0;
        foreach (var document in documents)
        {
            var chunks = DocumentChunker.Chunk(document);
            foreach (var batch in chunks.Chunk(BatchSize))
            {
                var vectors = await embeddings.GenerateAsync(batch.Select(c => c.Text), cancellationToken: ct);
                await store.UpsertAsync(batch, vectors.Select(v => v.Vector).ToList(), ct);
            }

            total += chunks.Count;
            logger.LogInformation("Ingested {Document} as {Chunks} chunks", document.Id, chunks.Count);
        }

        return total;
    }

    /// <summary>Loads markdown files with a small front-matter header (id, title, airline, language, version).</summary>
    public static IEnumerable<SourceDocument> LoadFromDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*.md"))
        {
            var raw = File.ReadAllText(file);
            var match = FrontMatter().Match(raw);
            var meta = match.Success
                ? match.Groups["meta"].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Split(':', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase)
                : [];
            var content = match.Success ? raw[match.Length..] : raw;

            yield return new SourceDocument(
                meta.GetValueOrDefault("id") ?? Path.GetFileNameWithoutExtension(file),
                meta.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(file),
                meta.GetValueOrDefault("airline") ?? "*",
                meta.GetValueOrDefault("language") ?? "en",
                meta.GetValueOrDefault("version") ?? "1",
                content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }

    [GeneratedRegex(@"^---\s*\n(?<meta>.*?)\n---\s*\n", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();
}

/// <summary>Seeds the corpus on startup when the collection is empty (dev convenience; prod uses the ops endpoint/CI job).</summary>
public sealed class CorpusSeeder(IServiceScopeFactory scopes, IHostEnvironment env, ILogger<CorpusSeeder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<QdrantVectorStore>();
            await store.EnsureCollectionAsync(stoppingToken);
            if (await store.CountAsync(stoppingToken) > 0)
            {
                return;
            }

            var ingestion = scope.ServiceProvider.GetRequiredService<CorpusIngestion>();
            var count = await ingestion.IngestAsync(CorpusIngestion.LoadFromDirectory(Path.Combine(env.ContentRootPath, "Corpus")), stoppingToken);
            logger.LogInformation("Seeded RAG corpus with {Count} chunks", count);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Corpus seeding skipped (is Qdrant running?)");
        }
    }
}
