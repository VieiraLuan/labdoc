using System.ClientModel;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using OpenAI;
using Qdrant.Client;
using LabDoc.Api.DTOs.Request;
using LabDoc.Api.Interfaces;
using LabDoc.Api.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddValidation();

//RAG
builder.Services.AddScoped<IRagIngest, RagIngestService>();
builder.Services.AddScoped<IRagAsk, RagAskService>();
builder.Services.AddScoped<IRagChunkService, RagChunkService>();

//Extractors
builder.Services.AddScoped<IPDFTextExtractor, PDFTextExtractor>();

//Master data extraction (no embeddings and no vector search: it is map-reduce
//over the whole document, not retrieval over a corpus).
builder.Services.AddSingleton<IWorkInstructionParser, WorkInstructionParser>();
builder.Services.AddSingleton<MasterDataSchema>();
builder.Services.AddScoped<IMasterDataExtractor, MasterDataExtractor>();

//LLM
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var endpoint = configuration["Llm:Endpoint"]
        ?? throw new InvalidOperationException("Llm:Endpoint is not configured.");
    var apiKey = configuration["Llm:ApiKey"]
        ?? throw new InvalidOperationException("Llm:ApiKey is not configured.");

    // The SDK defaults to a 100 second network timeout and then retries four
    // times. A local model generating a deeply nested JSON Schema blows past that,
    // so every attempt is killed mid-generation and the pass fails after ~7
    // minutes of work. The timeout has to match how slow local inference really is.
    var timeout = TimeSpan.FromSeconds(configuration.GetValue("Llm:TimeoutSeconds", 900));

    return new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
    {
        Endpoint = new Uri(endpoint),
        NetworkTimeout = timeout,
    });
});


builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var host = configuration["Qdrant:Host"] ?? "localhost";
    var port = configuration.GetValue("Qdrant:Port", 6334);
    return new QdrantClient(host, port);
});

builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
builder.Services.AddSingleton<IChatService, ChatService>();
builder.Services.AddSingleton<IVectorStore, QdrantVectorStore>();

//Connection Pool
builder.Services.AddSingleton(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
    return NpgsqlDataSource.Create(connectionString);
});

builder.Services.AddSingleton<IDocumentStore, PostgresDocumentStore>();


var app = builder.Build();

await app.Services.GetRequiredService<IDocumentStore>().EnsureSchemaAsync();
await app.Services.GetRequiredService<IVectorStore>().EnsureCollectionAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapPost("/api/v1/ask", async (
    AskRequest request,
    IRagAsk ragAsk,
    CancellationToken ct) =>
{
    var response = await ragAsk.AskAsync(request.Question, request.TopK, ct);
    return TypedResults.Ok(response);
});

app.MapGet("/api/v1/documents", async (IDocumentStore store, CancellationToken ct) =>
{
    var documents = await store.ListSummariesAsync(ct);
    return TypedResults.Ok(documents);
});

app.MapPost("/api/v1/extract", async (
    ExtractRequest request,
    IMasterDataExtractor extractor,
    CancellationToken ct) =>
{
    var response = await extractor.ExtractAsync(request.DocumentId, request.Passes, ct);
    return TypedResults.Ok(response);
});

app.MapPost("/api/v1/ingest", async (
    [FromForm] IngestRequest request,
    IRagIngest ragIngest,
    CancellationToken ct) =>
{
    var response = await ragIngest.IngestAsync(request.File, request.Description, request.SourceSystem, request.Force, ct);
    return TypedResults.Ok(response);
})
.DisableAntiforgery();


app.Run();