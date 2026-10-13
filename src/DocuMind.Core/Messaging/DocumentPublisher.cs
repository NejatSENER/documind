using System.Text.Json;
using RabbitMQ.Client;

namespace DocuMind.Core.Messaging;

public sealed class DocumentPublisher(IConnection connection)
{
    public async Task PublishAsync(DocumentUploaded message, CancellationToken ct = default)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await RabbitTopology.DeclareAsync(channel, ct);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = $"{message.DocumentId}:{Guid.NewGuid():N}",
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        await channel.BasicPublishAsync(RabbitTopology.Exchange, RabbitTopology.RoutingKey,
            mandatory: false, basicProperties: properties, body: body, cancellationToken: ct);
    }
}
