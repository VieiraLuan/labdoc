namespace LabDoc.Api.Interfaces;

public interface IPDFTextExtractor
{
    /// <summary>
    /// Extracts the text of a PDF. Takes a Stream — it knows nothing about HTTP or
    /// disk paths, so it serves an upload, a local file or a queue message alike.
    /// </summary>
    Task<string> ExtractTextAsync(Stream content, CancellationToken ct = default);
}
