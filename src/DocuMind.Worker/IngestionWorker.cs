using System.Text.Json;
using DocuMind.Core.Caching;
using DocuMind.Core.Ingestion;
using DocuMind.Core.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DocuMind.Worker;

public sealed class IngestionWorker(
    IConnection connection, IngestionService ingestion, SemanticCache cache, ILogger<IngestionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<DocumentUploaded>(delivery.Body.Span)
                    ?? throw new InvalidDataException("Message body is empty.");

                logger.LogInformation("Processing {DocumentId}", message.DocumentId);
                var chunks = await ingestion.IngestAsync(message.DocumentId, message.Text, stoppingToken);
                await cache.ClearAsync();

                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                logger.LogInformation("Done {DocumentId}: {Chunks} chunks stored", message.DocumentId, chunks);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed message {MessageId}, sending it to the dead-letter queue", delivery.BasicProperties.MessageId);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(RabbitTopology.Queue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Listening on queue {Queue}", RabbitTopology.Queue);

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
        finally { await channel.CloseAsync(); }
    }
}
