using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using DocuMind.Core.Storage;
using StackExchange.Redis;

namespace DocuMind.Core.Caching;

public record CachedAnswer(string Question, string Answer, IReadOnlyList<SearchHit> Sources, double Similarity);

public sealed class SemanticCache(IConnectionMultiplexer redis, double threshold = 0.95, TimeSpan? ttl = null)
{
    private const string Index = "idx:answers";
    private const string Prefix = "answer:";
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromHours(1);

    public async Task EnsureIndexAsync()
    {
        var db = redis.GetDatabase();
        try
        {
            await db.ExecuteAsync("FT.INFO", Index);
        }
        catch (RedisServerException)
        {
            await db.ExecuteAsync("FT.CREATE", Index, "ON", "HASH", "PREFIX", "1", Prefix,
                "SCHEMA", "embedding", "VECTOR", "HNSW", "6",
                "TYPE", "FLOAT32", "DIM", PgVectorChunkStore.Dimensions.ToString(), "DISTANCE_METRIC", "COSINE");
        }
    }

    public async Task<CachedAnswer?> FindAsync(ReadOnlyMemory<float> questionEmbedding)
    {
        var db = redis.GetDatabase();
        var result = (RedisResult[])(await db.ExecuteAsync("FT.SEARCH", Index,
            "*=>[KNN 1 @embedding $vec AS distance]",
            "PARAMS", "2", "vec", ToBytes(questionEmbedding),
            "RETURN", "4", "question", "answer", "sources", "distance",
            "DIALECT", "2"))!;

        if ((long)result[0] == 0) return null;

        var fields = (RedisResult[])result[2]!;
        var map = new Dictionary<string, string>();
        for (var i = 0; i < fields.Length; i += 2) map[(string)fields[i]!] = (string)fields[i + 1]!;

        var similarity = 1 - double.Parse(map["distance"], CultureInfo.InvariantCulture);
        if (similarity < threshold) return null;

        var sources = JsonSerializer.Deserialize<List<SearchHit>>(map["sources"]) ?? [];
        return new CachedAnswer(map["question"], map["answer"], sources, similarity);
    }

    public async Task StoreAsync(string question, ReadOnlyMemory<float> questionEmbedding, string answer, IReadOnlyList<SearchHit> sources)
    {
        var db = redis.GetDatabase();
        var key = Prefix + Guid.NewGuid().ToString("N");
        await db.HashSetAsync(key,
        [
            new HashEntry("question", question),
            new HashEntry("answer", answer),
            new HashEntry("sources", JsonSerializer.Serialize(sources)),
            new HashEntry("embedding", ToBytes(questionEmbedding)),
        ]);
        await db.KeyExpireAsync(key, _ttl);
    }

    public async Task ClearAsync()
    {
        var db = redis.GetDatabase();
        try { await db.ExecuteAsync("FT.DROPINDEX", Index, "DD"); }
        catch (RedisServerException) { }
        await EnsureIndexAsync();
    }

    private static byte[] ToBytes(ReadOnlyMemory<float> vector) => MemoryMarshal.AsBytes(vector.Span).ToArray();
}
