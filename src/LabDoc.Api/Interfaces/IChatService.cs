namespace LabDoc.Api.Interfaces;

public interface IChatService
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>
    /// Igual ao CompleteAsync, mas amarrado a um JSON Schema. O modelo nao
    /// "escolhe" obedecer: a decodificacao e restringida pela gramatica do
    /// schema, entao nao existe token possivel fora dele.
    /// </summary>
    Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        string schemaName,
        string jsonSchema,
        CancellationToken ct = default);
}
