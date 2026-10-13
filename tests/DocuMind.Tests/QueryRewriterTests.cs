using DocuMind.Core.Rag;
using Microsoft.Extensions.AI;

namespace DocuMind.Tests;

public class QueryRewriterTests
{
    [Fact]
    public async Task Without_history_the_question_is_used_as_is_and_the_llm_is_not_called()
    {
        var llm = new FakeChatClient("should not be used");
        var rewriter = new QueryRewriter(llm);

        var result = await rewriter.RewriteAsync("Yıllık iznim kaç gün?", history: []);

        Assert.Equal("Yıllık iznim kaç gün?", result);
        Assert.Empty(llm.Calls);
    }

    [Fact]
    public async Task With_history_the_llm_turns_a_follow_up_into_a_standalone_question()
    {
        var llm = new FakeChatClient("  Beş yılını dolduran çalışanın yıllık izni kaç gün?\n");
        var rewriter = new QueryRewriter(llm);
        List<ChatMessage> history =
        [
            new(ChatRole.User, "Yıllık iznim kaç gün?"),
            new(ChatRole.Assistant, "Yılda 14 iş günü. [K1]"),
        ];

        var result = await rewriter.RewriteAsync("Peki 5 yıldan sonra?", history);

        Assert.Equal("Beş yılını dolduran çalışanın yıllık izni kaç gün?", result);
        var sent = Assert.Single(llm.Calls);
        Assert.Contains(sent, m => m.Text.Contains("Yıllık iznim kaç gün?"));
        Assert.Contains("Peki 5 yıldan sonra?", sent[^1].Text);
    }
}
