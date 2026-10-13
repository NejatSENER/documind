using DocuMind.Core.Chunking;

namespace DocuMind.Tests;

public class TextChunkerTests
{
    private static string LongText(int sentences) =>
        string.Join(" ", Enumerable.Range(1, sentences).Select(i => $"This is sentence number {i}."));

    [Fact]
    public void Empty_text_returns_no_chunks()
    {
        var chunker = new TextChunker();

        var chunks = chunker.Split("   ");

        Assert.Empty(chunks);
    }

    [Fact]
    public void Short_text_returns_a_single_chunk()
    {
        var chunker = new TextChunker(maxChars: 100, overlapChars: 20);

        var chunks = chunker.Split("Employees get 14 days of annual leave.");

        var chunk = Assert.Single(chunks);
        Assert.Equal("Employees get 14 days of annual leave.", chunk.Text);
    }

    [Fact]
    public void No_chunk_is_longer_than_max_chars()
    {
        var chunker = new TextChunker(maxChars: 100, overlapChars: 20);

        var chunks = chunker.Split(LongText(50));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 100, $"Chunk {c.Index} has {c.Text.Length} chars"));
    }

    [Fact]
    public void Consecutive_chunks_overlap()
    {
        var chunker = new TextChunker(maxChars: 100, overlapChars: 20);

        var chunks = chunker.Split(LongText(50));

        var startOfSecond = chunks[1].Text[..10];
        Assert.Contains(startOfSecond, chunks[0].Text);
    }

    [Fact]
    public void Overlap_starts_at_a_sentence_boundary()
    {
        var chunker = new TextChunker(maxChars: 100, overlapChars: 40);

        var chunks = chunker.Split(LongText(50));

        Assert.All(chunks.Skip(1), c => Assert.StartsWith("This is sentence", c.Text));
    }

    [Fact]
    public void Paragraphs_are_kept_together_when_they_fit()
    {
        var chunker = new TextChunker(maxChars: 100, overlapChars: 20);
        var text = "Leave policy: 14 days per year.\n\nParking: first come, first served.";

        var chunks = chunker.Split(text);

        var chunk = Assert.Single(chunks);
        Assert.Contains("Leave policy", chunk.Text);
        Assert.Contains("Parking", chunk.Text);
    }
}
