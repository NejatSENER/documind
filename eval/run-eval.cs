#:project ../src/DocuMind.Core/DocuMind.Core.csproj
#:package OllamaSharp@*
#:property PublishAot=false
using System.Diagnostics;
using System.Text.Json;
using DocuMind.Core.Embeddings;
using DocuMind.Core.Rag;
using DocuMind.Core.Storage;
using Npgsql;
using OllamaSharp;
using Pgvector.Npgsql;

var golden = JsonSerializer.Deserialize<List<Case>>(File.ReadAllText("golden.json"), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

var dataSourceBuilder = new NpgsqlDataSourceBuilder("Host=localhost;Username=documind;Password=documind_dev;Database=documind");
dataSourceBuilder.UseVector();
await using var dataSource = dataSourceBuilder.Build();
var store = new PgVectorChunkStore(dataSource);
await store.EnsureSchemaAsync();

var ollama = new Uri("http://localhost:11434");
var embedder = new Embedder(new OllamaApiClient(ollama, "nomic-embed-text"));
var chat = new OllamaApiClient(ollama, "qwen2.5:7b");

var modes = new (string Name, RetrievalOptions Options)[]
{
    ("Yalnızca vektör", new RetrievalOptions(Hybrid: false, Rerank: false)),
    ("Hybrid (vektör + BM25, RRF)", new RetrievalOptions(Hybrid: true, Rerank: false)),
    ("Hybrid + LLM rerank", new RetrievalOptions(Hybrid: true, Rerank: true)),
};

Console.WriteLine($"{"Yöntem",-30} {"hit@1",8} {"hit@3",8} {"ort. süre",10}");
foreach (var (name, options) in modes)
{
    var rag = new RagChatService(embedder, store, chat, new QueryRewriter(chat), new LlmReranker(chat), options);
    int hit1 = 0, hit3 = 0; var watch = Stopwatch.StartNew();
    var misses = new List<string>();
    foreach (var c in golden)
    {
        var hits = await rag.RetrieveAsync(c.Question, await embedder.EmbedQueryAsync(c.Question));
        bool Match(SearchHit h) => h.DocumentId == c.Document && c.Chunks.Contains(h.ChunkIndex);
        if (hits.Count > 0 && Match(hits[0])) hit1++; else misses.Add(c.Question);
        if (hits.Take(3).Any(Match)) hit3++;
    }
    Console.WriteLine($"{name,-30} {hit1,4}/{golden.Count,-3} {hit3,4}/{golden.Count,-3} {watch.Elapsed.TotalSeconds / golden.Count,8:0.00} sn");
    foreach (var m in misses) Console.WriteLine($"{"",4}1. sırada kaçan: {m}");
}

record Case(string Question, string Document, int[] Chunks);
