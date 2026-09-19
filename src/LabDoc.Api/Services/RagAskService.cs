using System.Text;
using LabDoc.Api.DTOs.Response;
using LabDoc.Api.Interfaces;
using LabDoc.Api.Models;

namespace LabDoc.Api.Services;

public sealed class RagAskService : IRagAsk
{
    private const int ExcerptLength = 200;
    private const int MinTopK = 1;
    private const int MaxTopK = 20;

    private const string NoResultsAnswer =
        "Nao encontrei nada nos documentos indexados para responder a essa pergunta.";

    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IChatService _chatService;
    private readonly ILogger<RagAskService> _logger;

    private readonly string _systemPrompt;
    private readonly int _defaultTopK;

    public RagAskService(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IChatService chatService,
        IConfiguration configuration,
        ILogger<RagAskService> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _chatService = chatService;
        _logger = logger;

        _systemPrompt = configuration["Rag:SystemPrompt"]
            ?? throw new InvalidOperationException("Rag:SystemPrompt nao configurado.");
        _defaultTopK = configuration.GetValue("Rag:TopK", 4);
    }

    public async Task<AskResponse> AskAsync(string question, int? topK, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("A pergunta esta vazia.", nameof(question));
        }

        var effectiveTopK = Math.Clamp(topK ?? _defaultTopK, MinTopK, MaxTopK);


        var queryVector = await _embeddingService.EmbedQueryAsync(question, ct);

        var hits = await _vectorStore.SearchAsync(queryVector, effectiveTopK, ct);

        if (hits.Count == 0)
        {
            _logger.LogWarning("Busca sem resultados em uma collection possivelmente vazia.");
            return new AskResponse(NoResultsAnswer, [], effectiveTopK);
        }

        var userPrompt = BuildUserPrompt(question, hits);

        var answer = await _chatService.CompleteAsync(_systemPrompt, userPrompt, ct);

        var sources = new List<AskSource>(hits.Count);

        foreach (var hit in hits)
        {
            sources.Add(new AskSource(
                FileName: hit.FileName,
                ChunkIndex: hit.ChunkIndex,
                Score: hit.Score,
                Excerpt: Excerpt(hit.Text)));
        }

        _logger.LogInformation(
            "Pergunta respondida com {Count} trechos (melhor score {Score:F4}).",
            hits.Count, hits[0].Score);

        return new AskResponse(answer, sources, effectiveTopK);
    }

    private static string BuildUserPrompt(string question, IReadOnlyList<SearchHit> hits)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Contexto:");
        builder.AppendLine();

        for (var i = 0; i < hits.Count; i++)
        {
            builder.Append('[').Append(i + 1).Append("] ").AppendLine(hits[i].Text);
            builder.AppendLine();
        }

        builder.Append("Pergunta: ").Append(question);

        return builder.ToString();
    }

    private static string Excerpt(string text)
        => text.Length <= ExcerptLength
            ? text
            : string.Concat(text.AsSpan(0, ExcerptLength).TrimEnd(), "...");
}
