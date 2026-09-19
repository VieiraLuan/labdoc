namespace LabDoc.Api.DTOs.Response;

/// <summary>
/// Um trecho recuperado do Qdrant que sustentou a resposta.
/// Devolver isto e o que permite auditar se o LLM respondeu pelo documento
/// ou pela propria memoria.
/// </summary>
public sealed record AskSource(
    string FileName,
    int ChunkIndex,
    float Score,
    string Excerpt);
