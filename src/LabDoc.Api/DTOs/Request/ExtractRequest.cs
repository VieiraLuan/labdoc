using System.ComponentModel.DataAnnotations;

namespace LabDoc.Api.DTOs.Request;

public sealed record ExtractRequest(
    [property: Required] Guid DocumentId,

    // Empty = run every pass. Useful to inspect a single entity without
    // waiting for the whole document to be processed.
    IReadOnlyList<string>? Passes = null);
