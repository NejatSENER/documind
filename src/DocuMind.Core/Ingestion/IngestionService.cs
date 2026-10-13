using DocuMind.Core.Chunking;
using DocuMind.Core.Embeddings;
using DocuMind.Core.Storage;

namespace DocuMind.Core.Ingestion;

public sealed class IngestionService(TextChunker chunker, Embedder embedder, PgVectorChunkStore store)
{
    public async Task<int> IngestAsync(string documentId, string text, CancellationToken ct = default)
    {
        var chunks = chunker.Split(text);
        if (chunks.Count == 0) return 0;

        var vectors = await embedder.EmbedDocumentsAsync(chunks.Select(c => c.Text), ct);

        await store.UpsertAsync(
            chunks.Select((c, i) => new StoredChunk(documentId, c.Index, c.Text, vectors[i])), ct);

        return chunks.Count;
    }
}
