using DocuMind.Core.Agents;

namespace DocuMind.Tests;

public class ReviewResultTests
{
    [Fact]
    public void Reads_a_plain_json_verdict()
    {
        var result = ReviewResult.Parse("""{"approved": false, "reason": "20 gün kaynakta yok", "final_answer": "Bu bilgi dokümanlarda yok."}""");

        Assert.NotNull(result);
        Assert.False(result.Approved);
        Assert.Equal("Bu bilgi dokümanlarda yok.", result.FinalAnswer);
    }

    [Fact]
    public void Reads_json_wrapped_in_text_and_code_fences()
    {
        var result = ReviewResult.Parse("İşte değerlendirmem:\n```json\n{\"approved\": true, \"reason\": \"Kaynaklarla uyumlu\", \"final_answer\": \"14 gün [K1]\"}\n```");

        Assert.NotNull(result);
        Assert.True(result.Approved);
        Assert.Equal("14 gün [K1]", result.FinalAnswer);
    }

    [Fact]
    public void Returns_null_when_there_is_no_json()
    {
        Assert.Null(ReviewResult.Parse("Cevap bence doğru."));
    }
}
