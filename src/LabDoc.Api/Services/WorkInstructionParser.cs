using System.Text.RegularExpressions;
using LabDoc.Api.Interfaces;
using LabDoc.Api.Models;

namespace LabDoc.Api.Services;

/// <summary>
/// Splits the Work Instruction into numbered sections.
///
/// Unlike the RAG chunker, which cuts every N characters, the boundary here is
/// semantic: the master data contract is addressed by section ("section 8 ->
/// parameters", "section 11 -> limits"), so cutting in the middle of a section
/// would destroy exactly the structure that matters.
///
/// PDFTextExtractor joins words with spaces, which erases the page's line breaks,
/// so detection runs over running text through a regex configurable in
/// Extraction:SectionPattern.
/// </summary>
public sealed partial class WorkInstructionParser : IWorkInstructionParser
{
    private const int MinSectionsToAccept = 3;

    // Section number (1, 8, 11.2) followed by a title starting with a capital.
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

        // If the pattern did not recognise enough structure, handing over the whole
        // document beats handing over invented sections: the passes work fine on the
        // full document, they just cost more.
        if (matches.Count < MinSectionsToAccept)
        {
            _logger.LogWarning(
                "Only {Count} sections recognised: treating the document as a single section. " +
                "Tune Extraction:SectionPattern for the format of your documents.",
                matches.Count);

            return [new DocumentSection("0", "Whole document", normalized)];
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
            "Document split into {Count} sections: {Numbers}.",
            sections.Count, string.Join(", ", sections.Select(s => s.Number)));

        return sections;
    }
}
