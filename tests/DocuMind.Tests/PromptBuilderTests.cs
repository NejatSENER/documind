using DocuMind.Core.Rag;
using DocuMind.Core.Storage;
using Microsoft.Extensions.AI;

namespace DocuMind.Tests;

public class PromptBuilderTests
{
    private static readonly SearchHit[] Hits =
    [
        new("izin-politikasi.md", 3, "Babalara 5 iş günü babalık izni verilir.", 0.78),
        new("izin-politikasi.md", 1, "İzin talepleri İK portalından yapılır.", 0.71),
    ];

    [Fact]
    public void Starts_with_a_system_message_that_demands_grounding()
    {
        var messages = PromptBuilder.Build("Babalık izni kaç gün?", Hits);

        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains(PromptBuilder.NotFoundAnswer, messages[0].Text);
    }

    [Fact]
    public void User_message_contains_every_source_with_its_label_and_the_question()
    {
        var messages = PromptBuilder.Build("Babalık izni kaç gün?", Hits);

        var user = messages[^1];
        Assert.Equal(ChatRole.User, user.Role);
        Assert.Contains("[K1]", user.Text);
        Assert.Contains("[K2]", user.Text);
        Assert.Contains("Babalara 5 iş günü", user.Text);
        Assert.EndsWith("Soru: Babalık izni kaç gün?", user.Text);
    }

    [Fact]
    public void Labels_are_unique_even_when_documents_share_a_chunk_index()
    {
        SearchHit[] hits =
        [
            new("izin-politikasi.md", 0, "Yıllık izin 14 gündür.", 0.80),
            new("masraf-politikasi.md", 0, "Yemek limiti 1.200 TL'dir.", 0.70),
        ];

        var user = PromptBuilder.Build("Soru?", hits)[^1].Text!;

        Assert.Contains("[K1] (izin-politikasi.md, parça 0)", user);
        Assert.Contains("[K2] (masraf-politikasi.md, parça 0)", user);
        Assert.Equal("K2", PromptBuilder.LabelFor(1));
    }

    [Fact]
    public void Sources_are_wrapped_so_they_are_treated_as_data()
    {
        var messages = PromptBuilder.Build("Soru?", Hits);

        Assert.Contains("<kaynaklar>", messages[^1].Text);
        Assert.Contains("</kaynaklar>", messages[^1].Text);
    }
}
