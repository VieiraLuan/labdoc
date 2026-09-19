using System.Security.Cryptography;
using System.Text;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using LabDoc.Api.Interfaces;
using LabDoc.Api.Models;

namespace LabDoc.Api.Services;

public sealed class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly string _collection;
    private readonly string _embeddingModel;
    private readonly ulong _dimensions;

    public QdrantVectorStore(QdrantClient client, IConfiguration configuration, ILogger<QdrantVectorStore> logger)
    {
        _client = client;
        _logger = logger;
        _collection = configuration["Qdrant:Collection"]
            ?? throw new InvalidOperationException("Qdrant:Collection nao configurado.");
        _embeddingModel = configuration["Llm:EmbeddingModel"] ?? "desconhecido";
        _dimensions = (ulong)configuration.GetValue("Llm:EmbeddingDimensions", 1024);
    }

    public async Task EnsureCollectionAsync(CancellationToken ct = default)
    {
        if (!await _client.CollectionExistsAsync(_collection, ct))
        {
            await _client.CreateCollectionAsync(
                _collection,
                new VectorParams { Size = _dimensions, Distance = Distance.Cosine },
                cancellationToken: ct);

            _logger.LogInformation(
                "Collection '{Collection}' criada com {Dims} dimensoes, distancia Cosine.",
                _collection, _dimensions);
            return;
        }

        var info = await _client.GetCollectionInfoAsync(_collection, ct);
        var existing = info.Config.Params.VectorsConfig.Params.Size;

        if (existing != _dimensions)
        {
            throw new InvalidOperationException(
                $"A collection '{_collection}' foi criada com {existing} dimensoes, " +
                $"mas Llm:EmbeddingDimensions esta em {_dimensions}. " +
                "Use outra collection (o nome deve carregar o modelo) ou apague esta.");
        }

        _logger.LogInformation(
            "Collection '{Collection}' ja existe: {Dims} dimensoes, {Points} pontos.",
            _collection, existing, info.PointsCount);
    }

    public async Task<int> UpsertAsync(
        Guid documentId,
        string fileName,
        string? sourceSystem,
        IReadOnlyList<string> chunks,
        IReadOnlyList<float[]> vectors,
        CancellationToken ct = default)
    {
        if (chunks.Count != vectors.Count)
        {
            throw new InvalidOperationException(
                $"{chunks.Count} chunks para {vectors.Count} vetores: a correspondencia foi perdida.");
        }

        if (chunks.Count == 0)
        {
            return 0;
        }

        var points = new List<PointStruct>(chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            var point = new PointStruct
            {
                Id = new PointId { Uuid = DeterministicId(documentId, i).ToString() },
                Vectors = vectors[i]
            };
 
            point.Payload.Add("document_id", documentId.ToString());
            point.Payload.Add("file_name", fileName);
            point.Payload.Add("chunk_index", i);
            point.Payload.Add("text", chunks[i]);
            point.Payload.Add("source_system", sourceSystem ?? string.Empty);
            point.Payload.Add("embedding_model", _embeddingModel);

            points.Add(point);
        }

        await _client.UpsertAsync(_collection, points, cancellationToken: ct);

        _logger.LogInformation(
            "Upsert de {Count} pontos do documento {DocumentId} ({File}) em '{Collection}'.",
            points.Count, documentId, fileName, _collection);

        return points.Count;
    }

    private static Guid DeterministicId(Guid documentId, int chunkIndex)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{documentId}:{chunkIndex}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(float[] queryVector, int topK, CancellationToken ct = default)
    {
        if (topK <= 0)
        {
            return [];
        }
        if (queryVector.Length != (int)_dimensions)
        {
            throw new InvalidOperationException(
                $"O vetor da pergunta tem {queryVector.Length} dimensoes, " +
                $"mas a collection '{_collection}' espera {_dimensions}.");
        }

        var results = await _client.QueryAsync(
            _collection,
            query: queryVector,
            limit: (ulong)topK,
            payloadSelector: true,
            cancellationToken: ct);

        var hits = new List<SearchHit>(results.Count);

        foreach (var point in results)
        {
            hits.Add(new SearchHit(
                DocumentId: Guid.TryParse(ReadString(point, "document_id"), out var documentId)
                    ? documentId
                    : Guid.Empty,
                FileName: ReadString(point, "file_name"),
                ChunkIndex: (int)ReadInteger(point, "chunk_index"),
                Text: ReadString(point, "text"),
                Score: point.Score));
        }

        _logger.LogInformation(
            "Busca em '{Collection}': {Count} resultados, melhor score {Score:F4}.",
            _collection, hits.Count, hits.Count > 0 ? hits[0].Score : 0f);

        return hits;
    }

    private static string ReadString(ScoredPoint point, string key)
        => point.Payload.TryGetValue(key, out var value) ? value.StringValue : string.Empty;

    private static long ReadInteger(ScoredPoint point, string key)
        => point.Payload.TryGetValue(key, out var value) ? value.IntegerValue : 0L;
}
