namespace LabDoc.Api.DTOs.Response;

/// <summary>
/// Resultado de POST /api/v1/ingest.
/// </summary>
public sealed record IngestResponse(
    Guid DocumentId,
    string FileName,
    int ChunkCount,
    int pointsCount,
    bool Reused);
