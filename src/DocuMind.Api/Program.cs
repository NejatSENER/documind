using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using DocuMind.Api.Auth;
using DocuMind.Core;
using DocuMind.Core.Agents;
using DocuMind.Core.History;
using DocuMind.Core.Messaging;
using DocuMind.Core.Rag;
using DocuMind.Core.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var llm = builder.Configuration.GetSection("Llm").Get<LlmSettings>() ?? new LlmSettings();
var retrieval = builder.Configuration.GetSection("Rag").Get<RetrievalOptions>() ?? new RetrievalOptions();

// Services
builder.Services.AddLlm(llm);
builder.Services.AddDocuMindCore(builder.Configuration.GetConnectionString("Postgres")!, retrieval);
builder.Services.AddAgents();
builder.Services.AddConversationHistory(builder.Configuration.GetConnectionString("Mongo")!);
await builder.Services.AddSemanticCacheAsync(
    builder.Configuration.GetConnectionString("Redis")!,
    builder.Configuration.GetValue("Rag:CacheThreshold", 0.95));

var rabbit = new ConnectionFactory { Uri = new Uri(builder.Configuration.GetConnectionString("RabbitMQ")!) };
builder.Services.AddSingleton(await rabbit.CreateConnectionAsync("documind-api"));
builder.Services.AddSingleton<DocumentPublisher>();
builder.Services.AddOpenApi();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt settings are missing.");
var users = builder.Configuration.GetSection("Auth:Users").Get<DemoUser[]>() ?? [];
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = jwt.SigningKey,
        ClockSkew = TimeSpan.FromSeconds(30),
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("admin"));

var app = builder.Build();

await app.Services.GetRequiredService<PgVectorChunkStore>().EnsureSchemaAsync();

// Pipeline
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// Endpoints
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/auth/login", (LoginRequest request, TokenService tokens) =>
{
    var user = users.FirstOrDefault(u => u.Username == request.Username
        && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(u.Password), System.Text.Encoding.UTF8.GetBytes(request.Password ?? "")));
    if (user is null) return Results.Unauthorized();

    var (token, expiresAt) = tokens.Create(user);
    return Results.Ok(new { token, expiresAt, user.Username, user.Role });
});

app.MapPost("/documents", async (IngestRequest request, DocumentPublisher publisher, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.DocumentId) || string.IsNullOrWhiteSpace(request.Text))
        return Results.BadRequest(new { error = "documentId ve text alanları zorunlu." });

    await publisher.PublishAsync(new DocumentUploaded(request.DocumentId, request.Text), ct);
    return Results.Accepted(value: new { request.DocumentId, status = "queued" });
}).RequireAuthorization("Admin");

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
}).RequireAuthorization();

app.MapPost("/agent/stream", async (ChatRequest request, AgentOrchestrator agents, HttpResponse response, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    response.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";

    var result = await agents.RunAsync(request.Question, step => WriteEventAsync(response, "step", step, ct), ct);

    await WriteEventAsync(response, "sources",
        result.Sources.Select((s, i) => new { Label = PromptBuilder.LabelFor(i), s.DocumentId, s.ChunkIndex, s.Similarity }), ct);
    await WriteEventAsync(response, "token", result.Answer, ct);
    await WriteEventAsync(response, "done", new { }, ct);
}).RequireAuthorization();

app.Run();

static async Task WriteEventAsync<T>(HttpResponse response, string eventName, T data, CancellationToken ct)
{
    await response.WriteAsync($"event: {eventName}\ndata: {JsonSerializer.Serialize(data)}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

record LoginRequest(string Username, string? Password);
record IngestRequest(string DocumentId, string Text);
record ChatRequest(string Question, string? ConversationId = null);
