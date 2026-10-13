using RabbitMQ.Client;

namespace DocuMind.Core.Messaging;

public record DocumentUploaded(string DocumentId, string Text);

public static class RabbitTopology
{
    public const string Exchange = "documind.documents";
    public const string RoutingKey = "document.uploaded";
    public const string Queue = "documents.ingest";
    public const string DeadLetterExchange = "documind.dead-letter";
    public const string DeadLetterQueue = "documents.ingest.dead";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, routingKey: "", cancellationToken: ct);

        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange },
            cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, RoutingKey, cancellationToken: ct);
    }
}
