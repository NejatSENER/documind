using System.ClientModel;
using Azure.AI.OpenAI;
using DocuMind.Core.Embeddings;
using DocuMind.Core.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using OllamaSharp;

namespace DocuMind.Core;

public record LlmSettings
{
    public string Provider { get; init; } = "Ollama";
    public string Endpoint { get; init; } = "http://localhost:11434";
    public string? ApiKey { get; init; }
    public string ChatModel { get; init; } = "qwen2.5:7b";
    public string EmbeddingModel { get; init; } = "nomic-embed-text";
    public string DocumentPrefix { get; init; } = "search_document: ";
    public string QueryPrefix { get; init; } = "search_query: ";
}

public static class LlmProvider
{
    public static IServiceCollection AddLlm(this IServiceCollection services, LlmSettings settings)
    {
        var (chat, embeddings, kernel) = settings.Provider switch
        {
            "Ollama" => CreateOllama(settings),
            "AzureOpenAI" => CreateAzureOpenAI(settings),
            _ => throw new InvalidOperationException($"Unknown LLM provider '{settings.Provider}'."),
        };

        services.AddSingleton(chat);
        services.AddSingleton(embeddings);
        services.AddSingleton(kernel);
        services.AddSingleton(sp => new Embedder(
            sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            settings.DocumentPrefix, settings.QueryPrefix));
        return services;
    }

    private static (IChatClient, IEmbeddingGenerator<string, Embedding<float>>, Kernel) CreateOllama(LlmSettings s)
    {
        var endpoint = new Uri(s.Endpoint);
        return (
            new OllamaApiClient(endpoint, s.ChatModel),
            new OllamaApiClient(endpoint, s.EmbeddingModel),
            Kernel.CreateBuilder().AddOllamaChatCompletion(s.ChatModel, endpoint).Build());
    }

    private static (IChatClient, IEmbeddingGenerator<string, Embedding<float>>, Kernel) CreateAzureOpenAI(LlmSettings s)
    {
        var apiKey = s.ApiKey ?? throw new InvalidOperationException("Llm:ApiKey is required for Azure OpenAI.");
        var azure = new AzureOpenAIClient(new Uri(s.Endpoint), new ApiKeyCredential(apiKey));

        var embeddings = azure.GetEmbeddingClient(s.EmbeddingModel).AsIEmbeddingGenerator()
            .AsBuilder()
            .ConfigureOptions(options => options.Dimensions ??= PgVectorChunkStore.Dimensions)
            .Build();

        return (
            azure.GetChatClient(s.ChatModel).AsIChatClient(),
            embeddings,
            Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(s.ChatModel, s.Endpoint, apiKey).Build());
    }
}
