using System.Text.RegularExpressions;
using LabDoc.Api.Interfaces;
using LabDoc.Api.Models;

namespace LabDoc.Api.Services;

/// <summary>
/// Quebra a WI em secoes numeradas.
///
/// Diferente do chunking do RAG, que corta a cada N caracteres, aqui a fronteira
/// e semantica: o contrato de master data e endereçado por secao ("secao 8 ->
/// parametros", "secao 11 -> limites"), entao cortar no meio de uma secao
/// destruiria justamente a estrutura que interessa.
///
/// O PDFTextExtractor junta as palavras com espaco, o que apaga as quebras de
/// linha da pagina — por isso a deteccao roda sobre texto corrido, via regex
/// configuravel em Extraction:SectionPattern.
/// </summary>
public sealed partial class WorkInstructionParser : IWorkInstructionParser
{
    private const int MinSectionsToAccept = 3;

    // Numero da secao (1, 8, 11.2) seguido de um titulo que comeca com maiuscula.
    private const string DefaultPattern =
        @"(?<num>\b\d{1,2}(?:\.\d{1,2})*)[.)]?\s+(?<title>\p{Lu}[\p{L}\d][^\r\n]{2,70}?)(?=\s+(?:\p{Lu}|\d))";

    private readonly Regex _sectionRegex;
    private readonly ILogger<WorkInstructionParser> _logger;

    public WorkInstructionParser(IConfiguration configuration, ILogger<WorkInstructionParser> logger)
    {
        _logger = logger;
        var pattern = configuration["Extraction:SectionPattern"] ?? DefaultPattern;
        _sectionRegex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public IReadOnlyList<DocumentSection> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = WhitespaceRegex().Replace(text, " ").Trim();
        var matches = _sectionRegex.Matches(normalized);

        // Se o padrao nao reconheceu estrutura suficiente, e melhor entregar o
        // documento inteiro do que entregar secoes inventadas: as passadas
        // funcionam com o documento todo, so ficam mais caras.
        if (matches.Count < MinSectionsToAccept)
        {
            _logger.LogWarning(
                "Apenas {Count} secoes reconhecidas: tratando o documento como uma secao unica. " +
                "Ajuste Extraction:SectionPattern para o formato das suas WIs.",
                matches.Count);

            return [new DocumentSection("0", "Documento completo", normalized)];
        }

        var sections = new List<DocumentSection>(matches.Count);

        for (var i = 0; i < matches.Count; i++)
        {
            var current = matches[i];
            var start = current.Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : normalized.Length;

            sections.Add(new DocumentSection(
                Number: current.Groups["num"].Value,
                Title: current.Groups["title"].Value.Trim(),
                Text: normalized[start..end].Trim()));
        }

        _logger.LogInformation(
            "WI dividida em {Count} secoes: {Numbers}.",
            sections.Count, string.Join(", ", sections.Select(s => s.Number)));

        return sections;
    }
}
