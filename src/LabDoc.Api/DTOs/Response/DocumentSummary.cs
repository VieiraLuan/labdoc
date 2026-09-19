namespace LabDoc.Api.DTOs.Response;

/// <summary>
/// Visao de listagem de um documento ingerido.
///
/// HasFullText importa: a extracao de master data precisa do texto completo, e
/// documentos ingeridos antes da coluna full_text existir nao tem. Sem esse
/// campo o usuario so descobre quando a extracao falha.
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
