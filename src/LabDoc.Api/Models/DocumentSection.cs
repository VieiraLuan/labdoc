namespace LabDoc.Api.Models;

/// <summary>
/// A numbered section of the Work Instruction ("8", "11.2") with its text.
/// The number is what feeds the _source.section field of the master data payload.
/// </summary>
public sealed record DocumentSection(string Number, string Title, string Text);
