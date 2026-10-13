using System.ComponentModel;
using System.Text;
using DocuMind.Core.Embeddings;
using DocuMind.Core.Rag;
using DocuMind.Core.Storage;
using Microsoft.SemanticKernel;

namespace DocuMind.Core.Agents;

public record AgentStep(string Agent, string Action, string Detail);

public sealed class DocumentSearchPlugin(RagChatService rag, Embedder embedder, Func<AgentStep, Task> onStep)
{
    public List<SearchHit> Found { get; } = [];

    [KernelFunction("search_documents")]
    [Description("Şirket dokümanlarında (izin, masraf vb. politikalar) arama yapar ve en ilgili pasajları kaynak etiketleriyle döndürür.")]
    public async Task<string> SearchAsync(
        [Description("Aranacak, tek başına anlaşılır bir soru ya da anahtar kelimeler")] string query,
        CancellationToken ct = default)
    {
        var hits = await rag.RetrieveAsync(query, await embedder.EmbedQueryAsync(query, ct), ct);

        var result = new StringBuilder();
        foreach (var hit in hits)
        {
            var index = Found.FindIndex(f => f.DocumentId == hit.DocumentId && f.ChunkIndex == hit.ChunkIndex);
            if (index < 0) { Found.Add(hit); index = Found.Count - 1; }
            result.AppendLine($"[{PromptBuilder.LabelFor(index)}] ({hit.DocumentId}) {hit.Content.Replace('\n', ' ')}");
        }

        await onStep(new AgentStep("Araç", "search_documents", $"\"{query}\" → {hits.Count} pasaj"));
        return result.Length == 0 ? "Sonuç bulunamadı." : result.ToString();
    }
}
