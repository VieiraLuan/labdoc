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
    private readonly bool _chainPasses;
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
        // Switchable so the effect of chaining can be measured, not assumed:
        // turn it off and the harness reports the plain map-reduce baseline.
        _chainPasses = configuration.GetValue("Extraction:ChainPasses", true);
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

            var builder = new StringBuilder()
                .AppendLine(pass.Instruction)
                .AppendLine();

            if (_chainPasses)
            {
                AppendVocabulary(builder, pass, payload);
            }

            var userPrompt = builder
                .AppendLine("Document:")
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
    /// Injects the identifiers earlier passes already produced, so this pass reuses
    /// them instead of inventing its own.
    ///
    /// This is the seam that plain map-reduce lacks: independent calls share no
    /// vocabulary, so the same parameter comes back as CELL_DRIFT from one pass and
    /// KF_CELL_DRIFT from another, and nothing downstream can join them.
    /// </summary>
    private static void AppendVocabulary(StringBuilder builder, ExtractionPass pass, JsonObject payload)
    {
        if (pass.DependsOn is not { Count: > 0 })
        {
            return;
        }

        var lines = new List<string>();

        foreach (var name in pass.DependsOn)
        {
            var values = Vocabulary(name, payload[name]);

            if (values.Count > 0)
            {
                lines.Add($"  {name}: {string.Join(", ", values)}");
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        builder
            .AppendLine("Identifiers already extracted from THIS document. Reuse them exactly as")
            .AppendLine("written, and never introduce a different name for the same thing:")
            .AppendLine();

        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        builder.AppendLine();
    }

    private static IReadOnlyList<string> Vocabulary(string passName, JsonNode? extracted)
    {
        if (extracted is not JsonArray array)
        {
            return [];
        }

        // A parameter list is useful to the next pass as its rows, not as its own
        // id: what specifications need is which parameter/type pairs exist.
        if (passName == "parameterLists")
        {
            return array
                .OfType<JsonObject>()
                .SelectMany(list => list["items"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(item => $"{item["parameterId"]} ({item["parameterType"]})")
                .Distinct()
                .ToList();
        }

        var idField = MasterDataPasses.All.FirstOrDefault(p => p.Name == passName)?.IdField;

        if (idField is null)
        {
            return [];
        }

        return array
            .OfType<JsonObject>()
            .Select(item => item[idField]?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct()
            .ToList();
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
