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

            ALTER TABLE chunks ADD COLUMN IF NOT EXISTS content_tsv tsvector
                GENERATED ALWAYS AS (to_tsvector('turkish', content)) STORED;

            CREATE INDEX IF NOT EXISTS chunks_content_tsv_gin
                ON chunks USING gin (content_tsv);
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

        return await ReadHitsAsync(command, ct);
    }

    public async Task<IReadOnlyList<SearchHit>> HybridSearchAsync(
        ReadOnlyMemory<float> queryEmbedding, string queryText, int topK, int candidatesPerMethod = 10, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand("""
            WITH query AS (
                SELECT to_tsquery('simple', NULLIF(array_to_string(
                    tsvector_to_array(to_tsvector('turkish', $2)), ' | '), '')) AS q
            ),
            vector_ranked AS (
                SELECT id, ROW_NUMBER() OVER (ORDER BY embedding <=> $1) AS rank
                FROM chunks ORDER BY embedding <=> $1 LIMIT $3
            ),
            keyword_ranked AS (
                SELECT id, ROW_NUMBER() OVER (ORDER BY ts_rank_cd(content_tsv, query.q) DESC) AS rank
                FROM chunks, query
                WHERE content_tsv @@ query.q
                ORDER BY ts_rank_cd(content_tsv, query.q) DESC LIMIT $3
            )
            SELECT c.document_id, c.chunk_index, c.content, 1 - (c.embedding <=> $1) AS similarity
            FROM chunks c
            LEFT JOIN vector_ranked v ON v.id = c.id
            LEFT JOIN keyword_ranked k ON k.id = c.id
            WHERE v.id IS NOT NULL OR k.id IS NOT NULL
            ORDER BY COALESCE(1.0 / (60 + v.rank), 0) + COALESCE(1.0 / (60 + k.rank), 0) DESC
            LIMIT $4
            """);
        command.Parameters.Add(new() { Value = new Vector(queryEmbedding) });
        command.Parameters.Add(new() { Value = queryText });
        command.Parameters.Add(new() { Value = candidatesPerMethod });
        command.Parameters.Add(new() { Value = topK });

        return await ReadHitsAsync(command, ct);
    }

    private static async Task<IReadOnlyList<SearchHit>> ReadHitsAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var hits = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            hits.Add(new SearchHit(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetDouble(3)));
        return hits;
    }
}
