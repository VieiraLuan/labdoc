using System.Security.Cryptography;
using LabDoc.Api.DTOs.Response;
using LabDoc.Api.Interfaces;
using LabDoc.Api.Models;

namespace LabDoc.Api.Services;

public class RagIngestService : IRagIngest
{
    private const string PDF = "PDF";
    private const string DOCX = "DOCX";
    private const string TXT = "TXT";
    private const string CSV = "CSV";

    private readonly IRagChunkService _ragChunkService;
    private readonly IPDFTextExtractor _pdfTextExtractor;
    private readonly ILogger<RagIngestService> _logger;

    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentStore _documentStore;
    private readonly IConfiguration _configuration;

    public RagIngestService(
        IRagChunkService ragChunkService,
        IPDFTextExtractor pdfTextExtractor,
        ILogger<RagIngestService> logger,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IDocumentStore documentStore,
        IConfiguration configuration)
    {
        _ragChunkService = ragChunkService;
        _pdfTextExtractor = pdfTextExtractor;
        _logger = logger;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _documentStore = documentStore;
        _configuration = configuration;
    }

    public async Task<IngestResponse> IngestAsync(IFormFile file, string? description, string? sourceSystem, bool force, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            throw new ArgumentException("File is empty", nameof(file));
        }

        await _vectorStore.EnsureCollectionAsync(ct);

        var extension = Path.GetExtension(file.FileName).ToUpperInvariant().TrimStart('.');

        await using var upload = file.OpenReadStream();
        using var content = new MemoryStream();
        await upload.CopyToAsync(content, ct);

        content.Position = 0;
        var contentHash = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant();
        content.Position = 0;

        var chunkSize = _configuration.GetValue("Rag:ChunkSize", 800);
        var chunkOverlap = _configuration.GetValue("Rag:ChunkOverlap", 120);
        var embeddingModel = _configuration["Llm:EmbeddingModel"] ?? "desconhecido";
        var collectionName = _configuration["Qdrant:Collection"] ?? "desconhecida";

        // Consulta ANTES de extrair e embedar: se nada que afeta os vetores mudou,
        // eles ja estao no Qdrant e reprocessar seria puro desperdicio.
        var existing = await _documentStore.FindByContentHashAsync(contentHash, ct);

        if (!force
            && existing is not null
            && existing.EmbeddingModel == embeddingModel
            && existing.CollectionName == collectionName
            && existing.ChunkSize == chunkSize
            && existing.ChunkOverlap == chunkOverlap
            && existing.Status == DocumentRecord.StatusCompleted)
        {
            _logger.LogInformation(
                "Documento {Id} ja ingerido com a mesma configuracao (hash {Hash}): reaproveitando.",
                existing.Id, contentHash[..12]);

            return new IngestResponse(
                DocumentId: existing.Id,
                FileName: existing.FileName,
                ChunkCount: existing.ChunkCount,
                pointsCount: existing.ChunkCount,
                Reused: true);
        }

        var text = extension switch
        {
            PDF => await _pdfTextExtractor.ExtractTextAsync(content, ct),
            DOCX or TXT or CSV => throw new NotSupportedException(
                $"Extracao de {extension} ainda nao implementada."),
            _ => throw new ArgumentException(
                $"File type '{extension}' is not supported. Supported types are: {PDF}, {DOCX}, {TXT}, {CSV}",
                nameof(file))
        };

        var chunks = _ragChunkService.Split(text);

        var embeddings = await _embeddingService.EmbedDocumentsAsync(chunks, ct);

        // Chegou aqui: ou e documento novo, ou algo que afeta os vetores mudou.
        // Reaproveita o Id para sobrescrever os pontos em vez de duplicar.
        var documentId = existing?.Id ?? Guid.NewGuid();


        var upserted = await _vectorStore.UpsertAsync(
            documentId, file.FileName, sourceSystem, chunks, embeddings, ct);

        var now = DateTimeOffset.UtcNow;
        await _documentStore.SaveAsync(new DocumentRecord(
            Id: documentId,
            FileName: file.FileName,
            ContentHash: contentHash,
            Description: description,
            SourceSystem: sourceSystem,
            CharacterCount: text.Length,
            ChunkCount: chunks.Count,
            EmbeddingModel: embeddingModel,
            CollectionName: collectionName,
            ChunkSize: chunkSize,
            ChunkOverlap: chunkOverlap,
            Status: DocumentRecord.StatusCompleted,
            CreatedAt: now,
            UpdatedAt: now), ct);

        // A extracao de master data precisa do documento inteiro, e o RAG so guarda
        // ele fatiado no Qdrant. Salvar aqui e o que permite os dois pipelines
        // conviverem sobre o mesmo ingest.
        await _documentStore.SaveFullTextAsync(documentId, text, ct);

        IngestResponse response = new IngestResponse(
            DocumentId: documentId,
            FileName: file.FileName,
            ChunkCount: chunks.Count,
            pointsCount: upserted,
            Reused: false);

        return response;

    }
}
