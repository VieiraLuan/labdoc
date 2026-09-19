namespace LabDoc.Api.Models;

/// <summary>
/// Uma secao numerada da Work Instruction ("8", "11.2"), com o texto dela.
/// O numero e o que alimenta o campo _source.section do payload de master data.
/// </summary>
public sealed record DocumentSection(string Number, string Title, string Text);
