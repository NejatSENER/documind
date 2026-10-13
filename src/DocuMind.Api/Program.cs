using System.Text;
using System.Text.Json;
using DocuMind.Core;
using DocuMind.Core.History;
using DocuMind.Core.Messaging;
using DocuMind.Core.Rag;
using DocuMind.Core.Storage;
using Microsoft.Extensions.AI;
using OllamaSharp;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var ollama = builder.Configuration.GetSection("Ollama").Get<OllamaSettings>()
    ?? throw new InvalidOperationException("Ollama settings are missing.");
var retrieval = builder.Configuration.GetSection("Rag").Get<RetrievalOptions>() ?? new RetrievalOptions();

// Services
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new OllamaApiClient(new Uri(ollama.Endpoint), ollama.EmbeddingModel));
builder.Services.AddSingleton<IChatClient>(
    new OllamaApiClient(new Uri(ollama.Endpoint), ollama.ChatModel));
builder.Services.AddDocuMindCore(builder.Configuration.GetConnectionString("Postgres")!, retrieval);
builder.Services.AddConversationHistory(builder.Configuration.GetConnectionString("Mongo")!);
await builder.Services.AddSemanticCacheAsync(
    builder.Configuration.GetConnectionString("Redis")!,
    builder.Configuration.GetValue("Rag:CacheThreshold", 0.95));

var rabbit = new ConnectionFactory { Uri = new Uri(builder.Configuration.GetConnectionString("RabbitMQ")!) };
builder.Services.AddSingleton(await rabbit.CreateConnectionAsync("documind-api"));
builder.Services.AddSingleton<DocumentPublisher>();
builder.Services.AddOpenApi();

var app = builder.Build();

await app.Services.GetRequiredService<PgVectorChunkStore>().EnsureSchemaAsync();

// Pipeline
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseDefaultFiles();
app.UseStaticFiles();

// Endpoints
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/documents", async (IngestRequest request, DocumentPublisher publisher, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.DocumentId) || string.IsNullOrWhiteSpace(request.Text))
        return Results.BadRequest(new { error = "documentId ve text alanları zorunlu." });

    await publisher.PublishAsync(new DocumentUploaded(request.DocumentId, request.Text), ct);
    return Results.Accepted(value: new { request.DocumentId, status = "queued" });
});

app.MapPost("/chat/stream", async (
    ChatRequest request, RagChatService rag, ConversationStore history, HttpResponse response, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    response.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";

    var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
        ? Guid.NewGuid().ToString("N")
        : request.ConversationId;
    var pastMessages = await history.GetRecentAsync(conversationId, ct: ct);

    var answer = await rag.AskAsync(request.Question, pastMessages, ct);
    await WriteEventAsync(response, "conversation", new { Id = conversationId, answer.SearchQuestion }, ct);
    await WriteEventAsync(response, "cache", answer.CacheHit is { } hit
        ? new { Hit = true, hit.Similarity, hit.Question }
        : new { Hit = false, Similarity = 0d, Question = "" }, ct);
    await WriteEventAsync(response, "sources",
        answer.Sources.Select((s, i) => new { Label = PromptBuilder.LabelFor(i), s.DocumentId, s.ChunkIndex, s.Similarity }), ct);

    var fullAnswer = new StringBuilder();
    await foreach (var token in answer.Tokens.WithCancellation(ct))
    {
        fullAnswer.Append(token);
        await WriteEventAsync(response, "token", token, ct);
    }

    await history.AppendAsync(conversationId, request.Question, fullAnswer.ToString().Trim(), ct);

    await WriteEventAsync(response, "done", new { }, ct);
});

app.Run();

static async Task WriteEventAsync<T>(HttpResponse response, string eventName, T data, CancellationToken ct)
{
    await response.WriteAsync($"event: {eventName}\ndata: {JsonSerializer.Serialize(data)}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

record IngestRequest(string DocumentId, string Text);
record ChatRequest(string Question, string? ConversationId = null);
record OllamaSettings(string Endpoint, string ChatModel, string EmbeddingModel);
