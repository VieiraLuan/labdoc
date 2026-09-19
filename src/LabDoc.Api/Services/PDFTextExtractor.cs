using System.Text;
using LabDoc.Api.Interfaces;
using UglyToad.PdfPig;

namespace LabDoc.Api.Services;

public sealed class PDFTextExtractor(ILogger<PDFTextExtractor> logger) : IPDFTextExtractor
{
    public async Task<string> ExtractTextAsync(Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        if (buffer.Length == 0)
        {
            throw new InvalidOperationException("O arquivo enviado esta vazio.");
        }

        using var document = PdfDocument.Open(buffer);

        var builder = new StringBuilder();
        var pageCount = 0;

        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            pageCount++;

            var pageText = string.Join(' ', page.GetWords().Select(w => w.Text));
            if (string.IsNullOrWhiteSpace(pageText))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(pageText);
        }

        var text = builder.ToString();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                $"Nenhum texto extraido das {pageCount} paginas. O PDF provavelmente e digitalizado (imagem) e exigiria OCR.");
        }

        logger.LogInformation("PDF lido: {Pages} paginas, {Chars} caracteres.", pageCount, text.Length);

        return text;
    }
}