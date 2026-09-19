namespace LabDoc.Api.Interfaces;

public interface IPDFTextExtractor
{
    /// <summary>
    /// Extrai o texto de um PDF. Recebe Stream — nao conhece HTTP nem caminho em disco,
    /// entao serve tanto para upload quanto para arquivo local ou fila.
    /// </summary>
    Task<string> ExtractTextAsync(Stream content, CancellationToken ct = default);
}
