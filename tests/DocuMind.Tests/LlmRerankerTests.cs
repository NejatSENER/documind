using DocuMind.Core.Rag;
using DocuMind.Core.Storage;

namespace DocuMind.Tests;

public class LlmRerankerTests
{
    private static readonly SearchHit[] Candidates =
    [
        new("izin.md", 3, "Babalık izni 5 gün.", 0.70),
        new("izin.md", 1, "İzin talebi portaldan yapılır.", 0.69),
        new("izin.md", 0, "Yıllık izin 14 gündür.", 0.68),
    ];

    [Fact]
    public async Task Orders_candidates_by_the_scores_the_llm_returns()
    {
        var llm = new FakeChatClient("""{"1": 2, "2": 1, "3": 9}""");
        var reranker = new LlmReranker(llm);

        var result = await reranker.RerankAsync("Yıllık iznim kaç gün?", Candidates, topN: 2);

        Assert.Equal([0, 3], result.Select(h => h.ChunkIndex));
    }

    [Fact]
    public async Task Falls_back_to_the_original_order_when_the_llm_returns_invalid_json()
    {
        var llm = new FakeChatClient("Sanırım üçüncüsü en iyisi.");
        var reranker = new LlmReranker(llm);

        var result = await reranker.RerankAsync("Yıllık iznim kaç gün?", Candidates, topN: 2);

        Assert.Equal([3, 1], result.Select(h => h.ChunkIndex));
    }
}
