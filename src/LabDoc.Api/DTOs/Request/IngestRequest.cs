using System.ComponentModel.DataAnnotations;

namespace LabDoc.Api.DTOs.Request;

/// <summary>
/// Payload of POST /api/v1/ingest — multipart/form-data.
/// Init properties instead of a positional record: the minimal API form mapper
/// does not honour constructor parameter defaults and would require every field.
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
    /// Reprocess even when the document already exists with the same configuration.
    /// </summary>
    public bool Force { get; init; }
}
