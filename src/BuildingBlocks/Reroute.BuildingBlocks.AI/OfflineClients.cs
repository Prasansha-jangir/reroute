using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Reroute.BuildingBlocks.AI;

/// <summary>
/// Offline embedding generator using the "hashing trick" over word unigrams + bigrams, L2-normalised.
/// Not semantic, but lexically meaningful — so retrieval, ranking and the whole RAG pipeline work end-to-end
/// in CI and on a laptop with zero cost and zero API keys.
/// </summary>
public sealed partial class HashingEmbeddingGenerator(int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(v => new Embedding<float>(Embed(v)))));

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private float[] Embed(string text)
    {
        var vector = new float[dimensions];
        var tokens = Words().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            Add(vector, tokens[i], 1f);
            if (i > 0)
            {
                Add(vector, tokens[i - 1] + " " + tokens[i], 0.5f);
            }
        }

        var norm = MathF.Sqrt(vector.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }

    private void Add(float[] vector, string token, float weight)
    {
        var hash = (uint)StableHash(token);
        vector[hash % (uint)dimensions] += (hash & 0x8000_0000) == 0 ? weight : -weight;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var c in s)
            {
                h = (h ^ c) * 16777619;
            }

            return h;
        }
    }

    [GeneratedRegex(@"[\p{L}\p{N}€]+")]
    private static partial Regex Words();
}

/// <summary>Offline chat client: answers by quoting the most relevant retrieved source, with its citation.</summary>
public sealed partial class FakeChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer(messages))));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var word in Answer(messages).Split(' '))
        {
            await Task.Delay(15, cancellationToken); // feel like streaming
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static string Answer(IEnumerable<ChatMessage> messages)
    {
        var all = string.Join("\n", messages.Select(m => m.Text));
        var source = FirstSource().Match(all);
        return source.Success
            ? $"(Offline demo mode — set AI:Provider to use a real model.) The most relevant guidance is: \"{Truncate(source.Groups["text"].Value.Trim(), 350)}\" [{source.Groups["id"].Value}]"
            : "(Offline demo mode) I couldn't find relevant sources for that question.";
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    [GeneratedRegex("<source id=\"(?<id>\\d+)\"[^>]*>(?<text>.*?)</source>", RegexOptions.Singleline)]
    private static partial Regex FirstSource();
}
