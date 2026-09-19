using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using LabDoc.Api.DTOs.Response;
using LabDoc.Api.Interfaces;
using LabDoc.Api.Models;

namespace LabDoc.Api.Services;

/// <summary>
/// Extracts the master data payload of a Work Instruction.
///
/// This is NOT RAG. In RAG the search picks a few excerpts out of a large corpus;
/// here the document is already chosen and we want ALL of its content, so there
/// is no embedding and no vector search on this path. The pattern is map-reduce:
///
///   map    -> one LLM call per entity of the contract (MasterDataPasses)
///   reduce -> this service assembles the envelope and validates it against the schema
///
/// Each pass carries only its own schema fragment, which keeps every call small
/// and lets one fail without taking the others down.
/// </summary>
public sealed class MasterDataExtractor : IMasterDataExtractor
{
    private const string Contract = "lab.masterdata.payload/v1";
    private const string ExtractorName = "labdoc-wi-extractor/0.1.0";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly IDocumentStore _documentStore;
    private readonly IWorkInstructionParser _parser;
    private readonly IChatService _chatService;
    private readonly ILogger<MasterDataExtractor> _logger;

    private readonly string _systemPrompt;
    private readonly int _maxContextChars;
    private readonly JsonSchema? _contractSchema;

    public MasterDataExtractor(
        IDocumentStore documentStore,
        IWorkInstructionParser parser,
        IChatService chatService,
        MasterDataSchema schema,
        IConfiguration configuration,
        ILogger<MasterDataExtractor> logger)
    {
        _documentStore = documentStore;
        _parser = parser;
        _chatService = chatService;
        _logger = logger;

        _systemPrompt = configuration["Extraction:SystemPrompt"]
            ?? throw new InvalidOperationException("Extraction:SystemPrompt is not configured.");
        _maxContextChars = configuration.GetValue("Extraction:MaxContextChars", 24000);
        _contractSchema = schema.Schema;
    }

    public async Task<ExtractionResponse> ExtractAsync(
        Guid documentId,
        IReadOnlyList<string>? onlyPasses,
        CancellationToken ct = default)
    {
        var startedAt = Stopwatch.GetTimestamp();

        var document = await _documentStore.FindByIdAsync(documentId, ct)
            ?? throw new InvalidOperationException($"Document {documentId} was not found.");

        var text = await _documentStore.GetFullTextAsync(documentId, ct);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                $"Document {documentId} has no full text stored. " +
                "It was ingested before the full_text column existed: re-ingest with force=true.");
        }

        var sections = _parser.Parse(text);

        var payload = new JsonObject
        {
            ["$contract"] = Contract,
            ["source"] = new JsonObject
            {
                ["extractedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["extractor"] = ExtractorName,
                // Nothing else here: the contract declares additionalProperties:false
                // on source, so an extra field fails the whole payload.
            },
            // validateOnly: this payload is for human review, not for writing.
            ["options"] = new JsonObject
            {
                ["mode"] = "validateOnly",
                ["onExisting"] = "fail",
                ["onMissingReference"] = "fail",
                ["separator"] = "|",
            },
        };

        var passes = MasterDataPasses.All
            .Where(pass => onlyPasses is null || onlyPasses.Count == 0 || onlyPasses.Contains(pass.Name))
            .ToList();

        var results = new List<ExtractionPassResult>(passes.Count);

        foreach (var pass in passes)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await RunPassAsync(pass, payload, sections, text, ct));
        }

        var (schemaValid, schemaErrors) = Validate(payload);

        var seconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;

        _logger.LogInformation(
            "Extraction of {File} finished in {Seconds:F1}s: {Passes} passes, schema valid={Valid}.",
            document.FileName, seconds, results.Count, schemaValid);

        return new ExtractionResponse(
            DocumentId: documentId,
            FileName: document.FileName,
            SectionCount: sections.Count,
            Payload: payload,
            Passes: results,
            SchemaValid: schemaValid,
            SchemaErrors: schemaErrors,
            Seconds: seconds);
    }

    private async Task<ExtractionPassResult> RunPassAsync(
        ExtractionPass pass,
        JsonObject payload,
        IReadOnlyList<DocumentSection> sections,
        string fullText,
        CancellationToken ct)
    {
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var context = BuildContext(pass, sections, fullText);

            var userPrompt = new StringBuilder()
                .AppendLine(pass.Instruction)
                .AppendLine()
                .AppendLine("Documento:")
                .AppendLine()
                .Append(context)
                .ToString();

            var json = await _chatService.CompleteJsonAsync(
                _systemPrompt, userPrompt, pass.Name, pass.JsonSchema, ct);

            var node = JsonNode.Parse(json)
                ?? throw new InvalidOperationException("The model returned empty JSON.");

            // The "source" pass returns the metadata object directly; the others
            // return { "<name>": [...] } because the root must be an object.
            if (pass.Name == "source")
            {
                Merge((JsonObject)payload["source"]!, node.AsObject());
            }
            else
            {
                payload[pass.Name] = node[pass.Name]?.DeepClone();
            }

            var count = CountItems(payload[pass.Name] ?? node);
            var seconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;

            _logger.LogInformation(
                "Pass '{Pass}': {Count} items in {Seconds:F1}s.", pass.Name, count, seconds);

            return new ExtractionPassResult(pass.Name, count, seconds, Error: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failing pass does not take the others down: the payload comes out
            // incomplete and the error stays visible in the response, for review.
            _logger.LogError(ex, "Pass '{Pass}' failed.", pass.Name);

            return new ExtractionPassResult(
                pass.Name, 0, Stopwatch.GetElapsedTime(startedAt).TotalSeconds, ex.Message);
        }
    }

    /// <summary>
    /// Builds the context for a pass. If the requested sections exist, it sends only
    /// those (smaller call, less noise); otherwise it falls back to the whole document,
    /// because losing information is worse than spending tokens.
    /// </summary>
    private string BuildContext(ExtractionPass pass, IReadOnlyList<DocumentSection> sections, string fullText)
    {
        if (pass.Sections.Count > 0)
        {
            var selected = sections
                .Where(section => pass.Sections.Any(wanted =>
                    section.Number == wanted || section.Number.StartsWith(wanted + ".", StringComparison.Ordinal)))
                .ToList();

            if (selected.Count > 0)
            {
                return Truncate(string.Join("\n\n", selected.Select(s => s.Text)));
            }
        }

        return Truncate(fullText);
    }

    private string Truncate(string text)
        => text.Length <= _maxContextChars ? text : text[.._maxContextChars];

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var property in source)
        {
            target[property.Key] = property.Value?.DeepClone();
        }
    }

    private static int CountItems(JsonNode? node) => node switch
    {
        JsonArray array => array.Count,
        // references is an object of arrays: count the items across all of them.
        JsonObject obj => obj.Sum(p => p.Value is JsonArray inner ? inner.Count : 1),
        _ => 0,
    };

    private (bool Valid, IReadOnlyList<string> Errors) Validate(JsonObject payload)
    {
        if (_contractSchema is null)
        {
            return (true, []);
        }

        // JsonSchema.Net evaluates over JsonElement, not over JsonNode.
        using var document = JsonDocument.Parse(payload.ToJsonString());

        var results = _contractSchema.Evaluate(document.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });

        if (results.IsValid)
        {
            return (true, []);
        }

        var errors = results.Details
            .Where(detail => detail.Errors is { Count: > 0 })
            .SelectMany(detail => detail.Errors!.Select(
                error => $"{detail.InstanceLocation}: {error.Value}"))
            .Distinct()
            .Take(50)
            .ToList();

        return (false, errors);
    }

    public static string ToPrettyJson(JsonNode node) => node.ToJsonString(Indented);
}
