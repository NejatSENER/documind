using System.Runtime.CompilerServices;
using System.Text;
using DocuMind.Core.Caching;
using DocuMind.Core.Embeddings;
using DocuMind.Core.Storage;
using Microsoft.Extensions.AI;

namespace DocuMind.Core.Rag;

public record RagAnswer(IReadOnlyList<SearchHit> Sources, IAsyncEnumerable<string> Tokens, CachedAnswer? CacheHit = null);

public sealed class RagChatService(
    Embedder embedder, PgVectorChunkStore store, IChatClient chat, int topK = 3, SemanticCache? cache = null)
{
    public async Task<RagAnswer> AskAsync(string question, CancellationToken ct = default)
    {
        var queryVector = await embedder.EmbedQueryAsync(question, ct);

        if (cache is not null && await cache.FindAsync(queryVector) is { } hit)
            return new RagAnswer(hit.Sources, ReplayAsync(hit.Answer), hit);

        var hits = await store.SearchAsync(queryVector, topK, ct);
        var messages = PromptBuilder.Build(question, hits);
        return new RagAnswer(hits, StreamAndCacheAsync(question, queryVector, hits, messages, ct));
    }

    private async IAsyncEnumerable<string> StreamAndCacheAsync(
        string question, ReadOnlyMemory<float> queryVector, IReadOnlyList<SearchHit> hits,
        List<ChatMessage> messages, [EnumeratorCancellation] CancellationToken ct)
    {
        var fullAnswer = new StringBuilder();
        var options = new ChatOptions { Temperature = 0.1f };

        await foreach (var update in chat.GetStreamingResponseAsync(messages, options, ct))
        {
            if (string.IsNullOrEmpty(update.Text)) continue;
            fullAnswer.Append(update.Text);
            yield return update.Text;
        }

        var answer = fullAnswer.ToString().Trim();
        if (cache is not null && answer.Length > 0 && !answer.Contains(PromptBuilder.NotFoundAnswer))
            await cache.StoreAsync(question, queryVector, answer, hits);
    }

    private static async IAsyncEnumerable<string> ReplayAsync(string answer)
    {
        yield return answer;
        await Task.CompletedTask;
    }
}
