namespace LabDoc.Api.Interfaces;

public interface IChatService
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>
    /// Same as CompleteAsync, but bound to a JSON Schema. The model does not
    /// "choose" to comply: decoding is constrained by the schema's grammar, so
    /// no token outside it is even possible.
    /// </summary>
    Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        string schemaName,
        string jsonSchema,
        CancellationToken ct = default);
}
