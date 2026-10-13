using System.Text;
using Microsoft.Extensions.AI;

namespace DocuMind.Core.Rag;

public sealed class QueryRewriter(IChatClient chat)
{
    private const string Instruction = """
        Görevin, sohbet geçmişine bakarak kullanıcının son sorusunu geçmişi görmeden de anlaşılabilecek
        tek başına bir soruya dönüştürmek.
        Kurallar:
        - Asla cevap verme, bilgi ekleme ya da geçmişteki cevapları kullanma.
        - Çıktın soru işaretiyle biten tek bir soru olsun.
        - Son soru zaten tek başına anlaşılıyorsa aynen döndür.
        """;

    private static readonly ChatMessage[] Examples =
    [
        new(ChatRole.User, "Sohbet geçmişi:\nKullanıcı: Otel limiti ne kadar?\nAsistan: Gecelik en fazla 4.500 TL. [K1]\n\nSon soru: Daha pahalısı için ne yapmalıyım?"),
        new(ChatRole.Assistant, "Gecelik 4.500 TL'den pahalı bir otelde kalmak için ne yapmalıyım?"),
        new(ChatRole.User, "Sohbet geçmişi:\nKullanıcı: Doğum izni kaç hafta?\nAsistan: Toplam 16 hafta. [K2]\n\nSon soru: Babalar için durum ne?"),
        new(ChatRole.Assistant, "Babalara kaç gün doğum izni veriliyor?"),
    ];

    public async Task<string> RewriteAsync(string question, IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
    {
        if (history.Count == 0) return question;

        var transcript = new StringBuilder();
        foreach (var message in history)
            transcript.AppendLine($"{(message.Role == ChatRole.User ? "Kullanıcı" : "Asistan")}: {message.Text}");

        List<ChatMessage> messages =
        [
            new(ChatRole.System, Instruction),
            .. Examples,
            new(ChatRole.User, $"Sohbet geçmişi:\n{transcript}\nSon soru: {question}"),
        ];

        var response = await chat.GetResponseAsync(messages, new ChatOptions { Temperature = 0f }, ct);
        var rewritten = response.Text.Trim();
        return rewritten.Length > 0 ? rewritten : question;
    }
}
