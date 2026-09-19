
using LabDoc.Api.DTOs.Response;

namespace LabDoc.Api.Interfaces;
public interface IRagIngest
{
    Task<IngestResponse> IngestAsync(IFormFile file, string? description, string? sourceSystem, bool force, CancellationToken ct);
}