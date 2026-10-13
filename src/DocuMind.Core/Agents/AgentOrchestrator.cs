using System.Text;
using DocuMind.Core.Embeddings;
using DocuMind.Core.Rag;
using DocuMind.Core.Storage;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DocuMind.Core.Agents;

public record AgentResult(string Answer, IReadOnlyList<SearchHit> Sources, ReviewResult? Review);

public sealed class AgentOrchestrator(Kernel kernel, RagChatService rag, Embedder embedder)
{
    private const string ResearcherInstructions = $"""
        Sana bir soru ve o soru için yapılmış ilk aramanın sonuçları verilecek.
        Cevap sonuçlarda varsa yalnızca o pasajlara dayanarak kısa, Türkçe bir cevap yaz ve her bilginin sonuna pasaj etiketini ekle, örneğin [K1].
        Sonuçlar yetersizse search_documents aracıyla farklı kelimelerle ek arama yapabilirsin.
        Ek aramadan sonra da cevap yoksa yalnızca "{PromptBuilder.NotFoundAnswer}" yaz.
        """;

    private const string ReviewerInstructions = $$"""
        Sen titiz bir denetçisin. Sana bir soru, kaynak pasajlar ve bir taslak cevap verilecek.
        Taslaktaki her iddianın kaynaklarda açıkça geçip geçmediğini kontrol et.
        - Tüm iddialar kaynaklarla destekleniyorsa onayla ve taslağı aynen final_answer olarak döndür.
        - Desteklenmeyen bir iddia varsa onaylama ve final_answer'ı yalnızca kaynaklara dayanarak düzelt.
          Kaynaklar soruyu cevaplamıyorsa final_answer "{{PromptBuilder.NotFoundAnswer}}" olsun.
        Yalnızca şu biçimde JSON döndür:
        {"approved": true, "reason": "kısa gerekçe", "final_answer": "cevap"}
        """;

    public async Task<AgentResult> RunAsync(string question, Func<AgentStep, Task> onStep, CancellationToken ct = default)
    {
        var plugin = new DocumentSearchPlugin(rag, embedder, onStep);
        var researcherKernel = kernel.Clone();
        researcherKernel.Plugins.AddFromObject(plugin, "docs");

        var researcher = new ChatCompletionAgent
        {
            Name = "Arastirmaci",
            Instructions = ResearcherInstructions,
            Kernel = researcherKernel,
            Arguments = new KernelArguments(new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                ExtensionData = new Dictionary<string, object> { ["temperature"] = 0.1 },
            }),
        };

        await onStep(new AgentStep("Orkestratör", "ilk arama", question));
        var firstResults = await plugin.SearchAsync(question, ct);

        await onStep(new AgentStep("Araştırmacı", "başladı", question));
        var draft = await InvokeAsync(researcher,
            $"Soru: {question}\n\nİlk arama sonuçları:\n{firstResults}", ct);
        await onStep(new AgentStep("Araştırmacı", "taslak", draft));

        if (plugin.Found.Count == 0 || draft.Contains(PromptBuilder.NotFoundAnswer))
            return new AgentResult(draft.Length > 0 ? draft : PromptBuilder.NotFoundAnswer, plugin.Found, null);

        var reviewer = new ChatCompletionAgent
        {
            Name = "Denetci",
            Instructions = ReviewerInstructions,
            Kernel = kernel,
            Arguments = new KernelArguments(new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object> { ["temperature"] = 0.0 },
            }),
        };

        var sources = new StringBuilder();
        for (var i = 0; i < plugin.Found.Count; i++)
            sources.AppendLine($"[{PromptBuilder.LabelFor(i)}] {plugin.Found[i].Content.Replace('\n', ' ')}");

        var verdictText = await InvokeAsync(reviewer, $"Soru: {question}\n\nKaynaklar:\n{sources}\nTaslak cevap:\n{draft}", ct);
        var review = ReviewResult.Parse(verdictText);
        await onStep(new AgentStep("Denetçi", review is null ? "okunamadı" : review.Approved ? "onayladı" : "düzeltti",
            review?.Reason ?? verdictText));

        var answer = review is { Approved: false, FinalAnswer.Length: > 0 } ? review.FinalAnswer : draft;
        return new AgentResult(answer, plugin.Found, review);
    }

    private static async Task<string> InvokeAsync(ChatCompletionAgent agent, string input, CancellationToken ct)
    {
        var text = new StringBuilder();
        await foreach (var item in agent.InvokeAsync(new ChatMessageContent(AuthorRole.User, input), cancellationToken: ct))
            text.Append(item.Message.Content);
        return text.ToString().Trim();
    }
}
