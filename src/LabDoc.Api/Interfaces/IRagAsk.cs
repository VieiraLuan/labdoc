using LabDoc.Api.DTOs.Response;

namespace LabDoc.Api.Interfaces;

public interface IRagAsk
{
    Task<AskResponse> AskAsync(string question, int? topK, CancellationToken ct);
}
