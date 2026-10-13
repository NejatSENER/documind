using System.Text;
using System.Text.RegularExpressions;

namespace DocuMind.Core.Chunking;

public record Chunk(int Index, string Text);

public sealed class TextChunker(int maxChars = 1000, int overlapChars = 150)
{
    private static readonly string[] ParagraphSeparators = ["\r\n\r\n", "\n\n"];
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    public IReadOnlyList<Chunk> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var pieces = text
            .Split(ParagraphSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitLongParagraph);

        var chunks = new List<Chunk>();
        var current = new StringBuilder();

        foreach (var piece in pieces)
        {
            if (current.Length > 0 && current.Length + 1 + piece.Length > maxChars)
            {
                var finished = current.ToString();
                chunks.Add(new Chunk(chunks.Count, finished));
                current.Clear();

                var overlap = Tail(finished);
                if (overlap.Length + 1 + piece.Length <= maxChars) current.Append(overlap);
            }

            if (current.Length > 0) current.Append('\n');
            current.Append(piece);
        }

        if (current.Length > 0) chunks.Add(new Chunk(chunks.Count, current.ToString()));
        return chunks;
    }

    private IEnumerable<string> SplitLongParagraph(string paragraph)
    {
        if (paragraph.Length <= maxChars) return [paragraph];

        return SentenceEnd.Split(paragraph)
            .Where(s => s.Length > 0)
            .SelectMany(s => s.Length <= maxChars ? [s] : s.Chunk(maxChars).Select(c => new string(c)));
    }

    private string Tail(string text)
    {
        if (text.Length <= overlapChars) return "";

        var windowStart = text.Length - overlapChars;

        var sentenceStart = FindSentenceStart(text, windowStart);
        if (sentenceStart >= 0) return text[sentenceStart..];

        var wordBoundary = text.IndexOfAny([' ', '\n'], windowStart);
        return wordBoundary < 0 ? "" : text[(wordBoundary + 1)..];
    }

    private static int FindSentenceStart(string text, int from)
    {
        for (var i = from; i < text.Length - 1; i++)
        {
            var endsSentence = text[i] is '.' or '!' or '?' && char.IsWhiteSpace(text[i + 1]);
            if (endsSentence || text[i] == '\n')
            {
                var start = i + 1;
                while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
                if (start < text.Length) return start;
            }
        }
        return -1;
    }
}
