using Npgsql;
using Pgvector;

namespace DocuMind.Core.Storage;

public record StoredChunk(string DocumentId, int ChunkIndex, string Content, ReadOnlyMemory<float> Embedding);

public record SearchHit(string DocumentId, int ChunkIndex, string Content, double Similarity);

public sealed class PgVectorChunkStore(NpgsqlDataSource dataSource)
{
    public const int Dimensions = 768;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand($"""
            CREATE EXTENSION IF NOT EXISTS vector;

            CREATE TABLE IF NOT EXISTS chunks (
                id          BIGSERIAL PRIMARY KEY,
                document_id TEXT NOT NULL,
                chunk_index INT  NOT NULL,
                content     TEXT NOT NULL,
                embedding   VECTOR({Dimensions}) NOT NULL,
                UNIQUE (document_id, chunk_index)
            );

            CREATE INDEX IF NOT EXISTS chunks_embedding_hnsw
                ON chunks USING hnsw (embedding vector_cosine_ops);
            """, connection);
        await command.ExecuteNonQueryAsync(ct);
        await connection.ReloadTypesAsync(ct);
    }

    public async Task UpsertAsync(IEnumerable<StoredChunk> chunks, CancellationToken ct = default)
    {
        await using var batch = dataSource.CreateBatch();
        foreach (var chunk in chunks)
        {
            var command = new NpgsqlBatchCommand("""
                INSERT INTO chunks (document_id, chunk_index, content, embedding)
                VALUES ($1, $2, $3, $4)
                ON CONFLICT (document_id, chunk_index)
                DO UPDATE SET content = EXCLUDED.content, embedding = EXCLUDED.embedding
                """);
            command.Parameters.Add(new() { Value = chunk.DocumentId });
            command.Parameters.Add(new() { Value = chunk.ChunkIndex });
            command.Parameters.Add(new() { Value = chunk.Content });
            command.Parameters.Add(new() { Value = new Vector(chunk.Embedding) });
            batch.BatchCommands.Add(command);
        }
        await batch.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding, int topK, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT document_id, chunk_index, content, 1 - (embedding <=> $1) AS similarity
            FROM chunks
            ORDER BY embedding <=> $1
            LIMIT $2
            """);
        command.Parameters.Add(new() { Value = new Vector(queryEmbedding) });
        command.Parameters.Add(new() { Value = topK });

        var hits = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            hits.Add(new SearchHit(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetDouble(3)));
        return hits;
    }
}
