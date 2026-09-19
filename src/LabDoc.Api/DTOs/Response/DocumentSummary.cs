namespace LabDoc.Api.DTOs.Response;

/// <summary>
/// Listing view of an ingested document.
///
/// HasFullText matters: master data extraction needs the complete text, and
/// documents ingested before the full_text column existed do not have it.
/// Without this field the user only finds out when the extraction fails.
/// </summary>
public sealed record DocumentSummary(
    Guid Id,
    string FileName,
    string? Description,
    string? SourceSystem,
    int CharacterCount,
    int ChunkCount,
    string EmbeddingModel,
    string CollectionName,
    int ChunkSize,
    int ChunkOverlap,
    string Status,
    bool HasFullText,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
