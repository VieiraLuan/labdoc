namespace LabDoc.Api.DTOs.Response;

/// <summary>
/// Result of POST /api/v1/ingest.
/// </summary>
public sealed record IngestResponse(
    Guid DocumentId,
    string FileName,
    int ChunkCount,
    int pointsCount,
    bool Reused);
