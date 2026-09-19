namespace LabDoc.Api.Models;

public sealed record SearchHit(
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    string Text,
    float Score
);
