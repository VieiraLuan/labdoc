using System.ComponentModel.DataAnnotations;

namespace LabDoc.Api.DTOs.Request;

public sealed record ExtractRequest(
    [property: Required] Guid DocumentId,

    // Vazio = roda todas as passadas. Util para estudar uma entidade isolada
    // sem esperar o documento inteiro ser processado.
    IReadOnlyList<string>? Passes = null);
