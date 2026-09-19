using System.Text.Json.Nodes;

namespace LabDoc.Api.DTOs.Response;

public sealed record ExtractionPassResult(
    string Name,
    int ItemCount,
    double Seconds,
    string? Error);

public sealed record ExtractionResponse(
    Guid DocumentId,
    string FileName,
    int SectionCount,
    JsonNode Payload,
    IReadOnlyList<ExtractionPassResult> Passes,
    bool SchemaValid,
    IReadOnlyList<string> SchemaErrors,
    double Seconds);
