using DocuMind.Core.Caching;
using DocuMind.Core.Chunking;
using DocuMind.Core.Embeddings;
using DocuMind.Core.History;
using DocuMind.Core.Ingestion;
using DocuMind.Core.Rag;
using DocuMind.Core.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Npgsql;
using Pgvector.Npgsql;
using StackExchange.Redis;

namespace DocuMind.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddDocuMindCore(this IServiceCollection services, string postgresConnectionString, int topK = 3)
    {
        services.AddSingleton(_ =>
        {
            var dataSource = new NpgsqlDataSourceBuilder(postgresConnectionString);
            dataSource.UseVector();
            return dataSource.Build();
        });
        services.AddSingleton<TextChunker>();
        services.AddSingleton<Embedder>();
        services.AddSingleton<PgVectorChunkStore>();
        services.AddSingleton<IngestionService>();
        services.AddSingleton<QueryRewriter>();
        services.AddSingleton(sp => new RagChatService(
            sp.GetRequiredService<Embedder>(),
            sp.GetRequiredService<PgVectorChunkStore>(),
            sp.GetRequiredService<IChatClient>(),
            sp.GetRequiredService<QueryRewriter>(),
            topK,
            sp.GetService<SemanticCache>()));
        return services;
    }

    public static IServiceCollection AddConversationHistory(this IServiceCollection services, string mongoConnectionString)
    {
        services.AddSingleton<IMongoClient>(new MongoClient(mongoConnectionString));
        services.AddSingleton<ConversationStore>();
        return services;
    }

    public static async Task<IServiceCollection> AddSemanticCacheAsync(
        this IServiceCollection services, string redisConnectionString, double threshold)
    {
        var redis = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
        var cache = new SemanticCache(redis, threshold);
        await cache.EnsureIndexAsync();

        services.AddSingleton<IConnectionMultiplexer>(redis);
        services.AddSingleton(cache);
        return services;
    }
}
