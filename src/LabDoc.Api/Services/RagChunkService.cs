using System.Text.RegularExpressions;
using LabDoc.Api.Interfaces;

namespace LabDoc.Api.Services;

public partial class RagChunkService : IRagChunkService
{
    private const int MinChunkLength = 20;

    private readonly int _chunkSize;
    private readonly int _chunkOverlap;

    public RagChunkService(IConfiguration configuration)
    {
        _chunkSize = configuration.GetValue("Rag:ChunkSize", 800);
        _chunkOverlap = configuration.GetValue("Rag:ChunkOverlap", 120);

        if (_chunkSize <= MinChunkLength)
        {
            throw new InvalidOperationException(
                $"Rag:ChunkSize deve ser maior que {MinChunkLength}. Valor atual: {_chunkSize}.");
        }

        if (_chunkOverlap < 0 || _chunkOverlap >= _chunkSize)
        {
            throw new InvalidOperationException(
                $"Rag:ChunkOverlap deve estar entre 0 e {_chunkSize - 1}. Valor atual: {_chunkOverlap}.");
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    IReadOnlyList<string> IRagChunkService.Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = WhitespaceRegex().Replace(text, " ").Trim();

        var chunks = new List<string>();
        var start = 0;

        while (start < normalized.Length)
        {
            var end = Math.Min(start + _chunkSize, normalized.Length);

            if (end < normalized.Length)
            {
                var lastSpace = normalized.LastIndexOf(' ', end - 1, end - start);
                if (lastSpace > start + _chunkSize / 2)
                {
                    end = lastSpace;
                }
            }

            var chunk = normalized[start..end].Trim();
            if (chunk.Length > MinChunkLength)
            {
                chunks.Add(chunk);
            }

            if (end >= normalized.Length)
            {
                break;
            }

            start = Math.Max(end - _chunkOverlap, start + 1);
        }

        return chunks;
    }
}