using System.ComponentModel.DataAnnotations;

namespace LabDoc.Api.DTOs.Request;

/// <summary>
/// Payload de POST /api/v1/ingest — multipart/form-data.
/// Propriedades init em vez de record posicional: o mapeador de form do minimal API
/// nao honra defaults de parametro de construtor e exigiria todos os campos no request.
/// </summary>
public sealed record IngestRequest
{
    [Required]
    public IFormFile File { get; init; } = default!;

    [StringLength(500)]
    public string? Description { get; init; }

    [StringLength(100)]
    public string? SourceSystem { get; init; }

    /// <summary>
    /// Forca reprocessamento mesmo que o documento ja exista com a mesma configuracao.
    /// </summary>
    public bool Force { get; init; }
}
