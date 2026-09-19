namespace LabDoc.Api.DTOs.Response;

/// <summary>
/// An excerpt retrieved from Qdrant that backed the answer.
/// Returning these is what makes it auditable whether the LLM answered from the
/// document or from its own memory.
/// </summary>
public sealed record AskSource(
    string FileName,
    int ChunkIndex,
    float Score,
    string Excerpt);
