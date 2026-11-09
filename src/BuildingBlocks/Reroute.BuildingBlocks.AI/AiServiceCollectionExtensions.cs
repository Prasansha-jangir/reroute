using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace Reroute.BuildingBlocks.AI;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    /// <summary>Fake (offline, free, deterministic) | OpenAI | AzureOpenAI.</summary>
    public string Provider { get; set; } = "Fake";

    public string? ApiKey { get; set; }

    /// <summary>Azure: https://&lt;resource&gt;.openai.azure.com — the OpenAI-compatible /openai/v1/ path is appended.</summary>
    public string? Endpoint { get; set; }

    public string ChatModel { get; set; } = "gpt-4o-mini";

    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    public int EmbeddingDimensions { get; set; } = 1536;
}

/// <summary>
/// Provider-agnostic AI wiring via Microsoft.Extensions.AI. Application code depends only on
/// IChatClient / IEmbeddingGenerator; switching OpenAI ↔ Azure OpenAI ↔ offline fake is configuration.
/// Every call is traced with OpenTelemetry gen_ai.* semantic conventions (model, token usage, latency).
/// </summary>
public static class AiServiceCollectionExtensions
{
    public const string TelemetrySource = "Reroute.AI";

    public static IServiceCollection AddRerouteAI(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        services.AddSingleton(options);

        IChatClient chat;
        IEmbeddingGenerator<string, Embedding<float>> embeddings;

        if (options.Provider is "OpenAI" or "AzureOpenAI")
        {
            var clientOptions = new OpenAIClientOptions();
            if (options.Provider == "AzureOpenAI")
            {
                clientOptions.Endpoint = new Uri(options.Endpoint!.TrimEnd('/') + "/openai/v1/");
            }

            var client = new OpenAIClient(new ApiKeyCredential(options.ApiKey ?? throw new InvalidOperationException("AI:ApiKey is required.")), clientOptions);
            chat = client.GetChatClient(options.ChatModel).AsIChatClient();
            embeddings = client.GetEmbeddingClient(options.EmbeddingModel).AsIEmbeddingGenerator();
        }
        else
        {
            chat = new FakeChatClient();
            embeddings = new HashingEmbeddingGenerator(options.EmbeddingDimensions);
        }

        services.AddChatClient(chat).UseOpenTelemetry(sourceName: TelemetrySource);
        services.AddEmbeddingGenerator(embeddings).UseOpenTelemetry(sourceName: TelemetrySource);
        return services;
    }
}
