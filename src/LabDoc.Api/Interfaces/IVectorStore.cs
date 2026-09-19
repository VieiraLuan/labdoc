using LabDoc.Api.Models;

namespace LabDoc.Api.Interfaces;

public interface IVectorStore
{

    Task EnsureCollectionAsync(CancellationToken ct = default);


    Task<int> UpsertAsync(
        Guid documentId,
        string fileName,
        string? sourceSystem,
        IReadOnlyList<string> chunks,
        IReadOnlyList<float[]> vectors,
        CancellationToken ct = default);

    Task<IReadOnlyList<SearchHit>> SearchAsync(
    float[] queryVector, int topK, CancellationToken ct = default);

}
