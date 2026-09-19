using LabDoc.Api.DTOs.Response;

namespace LabDoc.Api.Interfaces;

public interface IMasterDataExtractor
{
    Task<ExtractionResponse> ExtractAsync(
        Guid documentId,
        IReadOnlyList<string>? onlyPasses,
        CancellationToken ct = default);
}
