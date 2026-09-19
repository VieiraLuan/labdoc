using LabDoc.Api.DTOs.Response;
using LabDoc.Api.Models;

namespace LabDoc.Api.Interfaces;

public interface IDocumentStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);

    Task<DocumentRecord?> FindByContentHashAsync(string contentHash, CancellationToken ct = default);

    Task SaveAsync(DocumentRecord document, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DocumentRecord>> ListAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DocumentSummary>> ListSummariesAsync(CancellationToken ct = default);

    Task<DocumentRecord?> FindByIdAsync(Guid id, CancellationToken ct = default);

    // A extracao precisa do documento INTEIRO. No RAG o texto so existe fatiado
    // no Qdrant, e remontar a partir dos chunks duplicaria as regioes de overlap.
    Task SaveFullTextAsync(Guid documentId, string text, CancellationToken ct = default);

    Task<string?> GetFullTextAsync(Guid documentId, CancellationToken ct = default);
}
