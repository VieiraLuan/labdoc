namespace LabDoc.Api.Models;

/// <summary>
/// One 'map' pass: a single LLM call that extracts ONE entity of the contract.
///
/// The core idea of the extraction is not to ask for the whole payload at once.
/// Each pass carries only its own entity schema and only the sections that matter,
/// so every call stays small, focused and verifiable on its own.
/// </summary>
/// <param name="Name">The entity key in the envelope: "units", "parameters"...</param>
/// <param name="Instruction">What to extract, in natural language.</param>
/// <param name="JsonSchema">The JSON Schema fragment that constrains the output.</param>
/// <param name="Sections">Document sections that feed the context. Empty = whole document.</param>
/// <param name="DependsOn">
/// Passes whose identifiers are injected into this prompt as a shared vocabulary.
/// Without it each pass invents its own names for the same thing — the measured
/// failure mode of a plain map-reduce, where one pass emits CELL_DRIFT and the
/// next KF_CELL_DRIFT for the same parameter.
/// </param>
/// <param name="IdField">The field other passes should reuse verbatim.</param>
public sealed record ExtractionPass(
    string Name,
    string Instruction,
    string JsonSchema,
    IReadOnlyList<string> Sections,
    IReadOnlyList<string>? DependsOn = null,
    string? IdField = null);
