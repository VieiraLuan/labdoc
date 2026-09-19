namespace LabDoc.Api.DTOs.Response;

public sealed record AskResponse(
    string Answer,
    IReadOnlyList<AskSource> Sources,
    int TopK);
