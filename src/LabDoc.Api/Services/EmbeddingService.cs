using OpenAI;
using OpenAI.Embeddings;
using LabDoc.Api.Interfaces;

namespace LabDoc.Api.Services;

public sealed class EmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _embeddings;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly string _model;
    private readonly int _expectedDimensions;
    private readonly int _batchSize;

    public EmbeddingService(OpenAIClient client, IConfiguration configuration, ILogger<EmbeddingService> logger)
    {
        _model = configuration["Llm:EmbeddingModel"]
            ?? throw new InvalidOperationException("Llm:EmbeddingModel is not configured.");
        _embeddings = client.GetEmbeddingClient(_model);
        _expectedDimensions = configuration.GetValue("Llm:EmbeddingDimensions", 1024);
        _batchSize = configuration.GetValue("Llm:EmbeddingBatchSize", 32);
        _logger = logger;

        if (_batchSize < 1)
        {
            throw new InvalidOperationException($"Llm:EmbeddingBatchSize must be >= 1. Current value: {_batchSize}.");
        }
    }

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        if (texts is null || texts.Count == 0)
        {
            return [];
        }

        var results = new List<float[]>(texts.Count);
        var batchNumber = 0;

        foreach (var batch in texts.Chunk(_batchSize))
        {
            ct.ThrowIfCancellationRequested();
            batchNumber++;

            var response = await _embeddings.GenerateEmbeddingsAsync(batch, cancellationToken: ct);

            foreach (var embedding in response.Value)
            {
                results.Add(embedding.ToFloats().ToArray());
            }

            _logger.LogDebug("Batch {Batch}: {Count} vectors ({Total}/{Expected}).",
                batchNumber, batch.Length, results.Count, texts.Count);
        }

        if (results.Count != texts.Count)
        {
            throw new InvalidOperationException(
                $"Model '{_model}' returned {results.Count} vectors for {texts.Count} texts. " +
                "The chunk/vector correspondence was lost.");
        }

        EnsureDimensions(results[0]);

        return results;
    }

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("An empty text cannot be embedded.", nameof(text));
        }

        // Kept separate from EmbedDocumentsAsync on purpose: asymmetric models
        // (nomic, E5) need a different prefix here. bge-m3 does not.
        var response = await _embeddings.GenerateEmbeddingAsync(text, cancellationToken: ct);
        var vector = response.Value.ToFloats().ToArray();

        EnsureDimensions(vector);

        return vector;
    }

    private void EnsureDimensions(float[] vector)
    {
        if (vector.Length != _expectedDimensions)
        {
            throw new InvalidOperationException(
                $"Model '{_model}' returned vectors of {vector.Length} dimensions, " +
                $"mas Llm:EmbeddingDimensions esta em {_expectedDimensions}. " +
                "Corrija a configuracao ou a collection do Qdrant vai rejeitar os pontos.");
        }
    }
}
