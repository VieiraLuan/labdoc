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
/// Extrai o payload de master data de uma Work Instruction.
///
/// Isto NAO e RAG. No RAG a busca escolhe alguns trechos de um corpus grande;
/// aqui o documento ja esta escolhido e queremos o conteudo INTEIRO dele, entao
/// nao ha embedding nem busca vetorial no caminho. O padrao e map-reduce:
///
///   map    -> uma chamada ao LLM por entidade do contrato (MasterDataPasses)
///   reduce -> este servico monta o envelope e valida contra o JSON Schema
///
/// Cada passada leva so o proprio fragmento de schema, o que mantem cada chamada
/// pequena e permite que uma falhe sem derrubar as outras.
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
            ?? throw new InvalidOperationException("Extraction:SystemPrompt nao configurado.");
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
            ?? throw new InvalidOperationException($"Documento {documentId} nao encontrado.");

        var text = await _documentStore.GetFullTextAsync(documentId, ct);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                $"O documento {documentId} nao tem texto completo salvo. " +
                "Ele foi ingerido antes da coluna full_text existir: re-ingira com force=true.");
        }

        var sections = _parser.Parse(text);

        var payload = new JsonObject
        {
            ["$contract"] = Contract,
            ["source"] = new JsonObject
            {
                ["extractedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["extractor"] = ExtractorName,
                // Nada mais aqui: o contrato declara additionalProperties:false
                // em source, entao campo extra reprova o payload inteiro.
            },
            // validateOnly: este payload e para revisao humana, nao para escrita.
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
            "Extracao de {File} concluida em {Seconds:F1}s: {Passes} passadas, schema valido={Valid}.",
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
                ?? throw new InvalidOperationException("O modelo devolveu JSON vazio.");

            // A passada "source" devolve o objeto de metadados direto; as outras
            // devolvem { "<nome>": [...] } por causa da exigencia de raiz-objeto.
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
                "Passada '{Pass}': {Count} itens em {Seconds:F1}s.", pass.Name, count, seconds);

            return new ExtractionPassResult(pass.Name, count, seconds, Error: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Uma passada que falha nao derruba as outras: o payload sai
            // incompleto e o erro fica visivel na resposta, para revisao.
            _logger.LogError(ex, "Passada '{Pass}' falhou.", pass.Name);

            return new ExtractionPassResult(
                pass.Name, 0, Stopwatch.GetElapsedTime(startedAt).TotalSeconds, ex.Message);
        }
    }

    /// <summary>
    /// Monta o contexto da passada. Se as secoes pedidas existirem, manda so elas
    /// (chamada menor, menos ruido); senao cai para o documento inteiro, porque
    /// perder informacao e pior do que gastar tokens.
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
        // references e um objeto de arrays: conta os itens de todas elas.
        JsonObject obj => obj.Sum(p => p.Value is JsonArray inner ? inner.Count : 1),
        _ => 0,
    };

    private (bool Valid, IReadOnlyList<string> Errors) Validate(JsonObject payload)
    {
        if (_contractSchema is null)
        {
            return (true, []);
        }

        // O JsonSchema.Net avalia sobre JsonElement, nao sobre JsonNode.
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
