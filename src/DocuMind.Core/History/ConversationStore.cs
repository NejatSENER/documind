using Microsoft.Extensions.AI;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace DocuMind.Core.History;

public sealed class Conversation
{
    [BsonId] public string Id { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<ConversationMessage> Messages { get; set; } = [];
}

public sealed class ConversationMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime At { get; set; }
}

public sealed class ConversationStore(IMongoClient client)
{
    private readonly IMongoCollection<Conversation> _conversations =
        client.GetDatabase("documind").GetCollection<Conversation>("conversations");

    public async Task<IReadOnlyList<ChatMessage>> GetRecentAsync(string conversationId, int maxMessages = 6, CancellationToken ct = default)
    {
        var conversation = await _conversations.Find(c => c.Id == conversationId).FirstOrDefaultAsync(ct);
        if (conversation is null) return [];

        return conversation.Messages
            .TakeLast(maxMessages)
            .Select(m => new ChatMessage(m.Role == "user" ? ChatRole.User : ChatRole.Assistant, m.Content))
            .ToList();
    }

    public Task AppendAsync(string conversationId, string question, string answer, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var update = Builders<Conversation>.Update
            .SetOnInsert(c => c.CreatedAt, now)
            .PushEach(c => c.Messages,
            [
                new ConversationMessage { Role = "user", Content = question, At = now },
                new ConversationMessage { Role = "assistant", Content = answer, At = now },
            ]);

        return _conversations.UpdateOneAsync(c => c.Id == conversationId, update, new UpdateOptions { IsUpsert = true }, ct);
    }
}
