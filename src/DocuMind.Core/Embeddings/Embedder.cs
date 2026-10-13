using Microsoft.Extensions.AI;

namespace DocuMind.Core.Embeddings;

public sealed class Embedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    string documentPrefix = "search_document: ",
    string queryPrefix = "search_query: ")
{
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        var embeddings = await generator.GenerateAsync(texts.Select(t => documentPrefix + t), cancellationToken: ct);
        return embeddings.Select(e => e.Vector).ToList();
    }

    public async Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken ct = default)
    {
        var embeddings = await generator.GenerateAsync([queryPrefix + query], cancellationToken: ct);
        return embeddings[0].Vector;
    }
}
