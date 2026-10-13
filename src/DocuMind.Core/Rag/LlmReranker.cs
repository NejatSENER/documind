using System.Text;
using System.Text.Json;
using DocuMind.Core.Storage;
using Microsoft.Extensions.AI;

namespace DocuMind.Core.Rag;

public sealed class LlmReranker(IChatClient chat)
{
    private const string Instruction = """
        Sana bir soru ve numaralı pasajlar verilecek. Her pasajın soruyu cevaplamak için ne kadar faydalı
        olduğunu 0 ile 10 arasında puanla. 10: soruyu doğrudan cevaplıyor, 0: alakasız.
        Yalnızca JSON döndür, örnek: {"1": 7, "2": 0, "3": 10}
        """;

    public async Task<IReadOnlyList<SearchHit>> RerankAsync(
        string question, IReadOnlyList<SearchHit> candidates, int topN, CancellationToken ct = default)
    {
        if (candidates.Count <= 1) return candidates.Take(topN).ToList();

        var passages = new StringBuilder();
        for (var i = 0; i < candidates.Count; i++)
            passages.AppendLine($"[{i + 1}] {candidates[i].Content.Replace('\n', ' ')}");

        List<ChatMessage> messages =
        [
            new(ChatRole.System, Instruction),
            new(ChatRole.User, $"Soru: {question}\n\nPasajlar:\n{passages}"),
        ];
        var options = new ChatOptions { Temperature = 0f, ResponseFormat = ChatResponseFormat.Json };
        var response = await chat.GetResponseAsync(messages, options, ct);

        var scores = TryParseScores(response.Text, candidates.Count);
        if (scores is null) return candidates.Take(topN).ToList();

        return candidates
            .Select((hit, i) => (hit, score: scores[i], i))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.i)
            .Take(topN)
            .Select(x => x.hit)
            .ToList();
    }

    private static double[]? TryParseScores(string json, int count)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var scores = new double[count];
            for (var i = 0; i < count; i++)
                scores[i] = document.RootElement.TryGetProperty((i + 1).ToString(), out var value) && value.TryGetDouble(out var score)
                    ? score
                    : 0;
            return scores;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
