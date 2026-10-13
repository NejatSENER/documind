using DocuMind.Core;
using DocuMind.Worker;
using RabbitMQ.Client;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLlm(builder.Configuration.GetSection("Llm").Get<LlmSettings>() ?? new LlmSettings());
builder.Services.AddDocuMindCore(builder.Configuration.GetConnectionString("Postgres")!);
await builder.Services.AddSemanticCacheAsync(builder.Configuration.GetConnectionString("Redis")!, threshold: 0.95);

var rabbit = new ConnectionFactory { Uri = new Uri(builder.Configuration.GetConnectionString("RabbitMQ")!) };
builder.Services.AddSingleton(await rabbit.CreateConnectionAsync("documind-worker"));

builder.Services.AddHostedService<IngestionWorker>();

var host = builder.Build();
host.Run();
