using System.Text.Json;

namespace DocuMind.Core.Agents;

public record ReviewResult(bool Approved, string Reason, string FinalAnswer)
{
    public static ReviewResult? Parse(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;
            return new ReviewResult(
                root.TryGetProperty("approved", out var approved) && approved.ValueKind == JsonValueKind.True,
                root.TryGetProperty("reason", out var reason) ? reason.GetString() ?? "" : "",
                root.TryGetProperty("final_answer", out var answer) ? answer.GetString() ?? "" : "");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
