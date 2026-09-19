namespace LabDoc.Api.Models;


public sealed record DocumentRecord(
    Guid Id,
    string FileName,
    string ContentHash,
    string? Description,
    string? SourceSystem,
    int CharacterCount,
    int ChunkCount,
    string EmbeddingModel,
    string CollectionName,
    int ChunkSize,
    int ChunkOverlap,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public const string StatusPending = "pending";
    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";
}
