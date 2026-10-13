using DocuMind.Core;
using DocuMind.Worker;
using Microsoft.Extensions.AI;
using OllamaSharp;
using RabbitMQ.Client;

var builder = Host.CreateApplicationBuilder(args);

var ollama = builder.Configuration.GetSection("Ollama");
var ollamaUri = new Uri(ollama["Endpoint"]!);

builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new OllamaApiClient(ollamaUri, ollama["EmbeddingModel"]!));
builder.Services.AddSingleton<IChatClient>(new OllamaApiClient(ollamaUri, ollama["ChatModel"]!));
builder.Services.AddDocuMindCore(builder.Configuration.GetConnectionString("Postgres")!);
await builder.Services.AddSemanticCacheAsync(builder.Configuration.GetConnectionString("Redis")!, threshold: 0.95);

var rabbit = new ConnectionFactory { Uri = new Uri(builder.Configuration.GetConnectionString("RabbitMQ")!) };
builder.Services.AddSingleton(await rabbit.CreateConnectionAsync("documind-worker"));

builder.Services.AddHostedService<IngestionWorker>();

var host = builder.Build();
host.Run();
