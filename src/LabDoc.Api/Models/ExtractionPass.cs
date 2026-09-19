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
public sealed record ExtractionPass(
    string Name,
    string Instruction,
    string JsonSchema,
    IReadOnlyList<string> Sections);
