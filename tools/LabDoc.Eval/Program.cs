using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LabDoc.Eval;

// Evaluation harness.
//
// Without a golden set, a change to a prompt, a schema or a model is guesswork:
// the output looks different, and nobody can say whether it got better. This
// ingests each sample that has a hand-written *.expected.json next to it, runs
// the extraction through the API, and grades the result.

var api = Arg("--api") ?? "http://localhost:8081";
var samplesDir = Arg("--samples") ?? "samples";
var reportPath = Arg("--report");
// Re-scoring should not cost another extraction run: each payload is kept, and
// --offline grades the saved ones so the harness itself can be iterated on.
var offline = args.Contains("--offline");
var payloadDir = Arg("--payloads") ?? "docs/payloads";
var passes = Arg("--passes")?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];

var expectedFiles = Directory.Exists(samplesDir)
    ? Directory.GetFiles(samplesDir, "*.expected.json").OrderBy(f => f).ToArray()
    : [];

if (expectedFiles.Length == 0)
{
    Console.Error.WriteLine($"No *.expected.json found in '{samplesDir}'.");
    return 1;
}

using var http = new HttpClient { BaseAddress = new Uri(api), Timeout = TimeSpan.FromHours(1) };

var scores = new List<DocumentScore>();

foreach (var expectedFile in expectedFiles)
{
    var name = Path.GetFileName(expectedFile).Replace(".expected.json", "");
    var pdf = Path.Combine(samplesDir, name + ".pdf");

    if (!File.Exists(pdf))
    {
        Console.Error.WriteLine($"skipped {name}: {pdf} not found");
        continue;
    }

    Console.WriteLine($"→ {name}");

    var payloadFile = Path.Combine(payloadDir, name + ".actual.json");
    JsonNode extraction;
    double seconds;

    if (offline)
    {
        if (!File.Exists(payloadFile))
        {
            Console.Error.WriteLine($"  no saved payload at {payloadFile}");
            continue;
        }

        extraction = JsonNode.Parse(File.ReadAllText(payloadFile))!;
        seconds = extraction["seconds"]?.GetValue<double>() ?? 0;
        Console.WriteLine($"  scored from {payloadFile}");
    }
    else
    {
        var documentId = await IngestAsync(pdf);
        Console.WriteLine($"  ingested as {documentId}");

        var started = Stopwatch.GetTimestamp();
        extraction = await ExtractAsync(documentId, passes);
        seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;

        Directory.CreateDirectory(payloadDir);
        File.WriteAllText(payloadFile, extraction.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    var expected = JsonNode.Parse(File.ReadAllText(expectedFile));
    var actual = extraction["payload"];

    // Order matters: entities that define identifiers are compared first, and the
    // aliases they discover translate the keys of everything compared after them.
    var aliases = new Dictionary<string, IReadOnlyDictionary<string, string>>();
    var entities = new List<EntityScore>();

    foreach (var spec in EntitySpec.All)
    {
        var score = Comparer.Compare(spec, expected, actual, aliases);

        if (spec.AliasField is not null && score.Aliases.Count > 0)
        {
            aliases[spec.AliasField] = score.Aliases;
        }

        // An entity the golden says nothing about is not being evaluated.
        if (score.Expected > 0)
        {
            entities.Add(score);
        }
    }

    // A pass reports failure by putting a message in "error"; null means it ran.
    var failed = extraction["passes"]?.AsArray()
        .Where(p => p?["error"]?.GetValueKind() == JsonValueKind.String)
        .Select(p => p!["name"]!.GetValue<string>())
        .ToList() ?? [];

    scores.Add(new DocumentScore(
        Document: name,
        SchemaValid: extraction["schemaValid"]?.GetValue<bool>() ?? false,
        Seconds: seconds,
        Entities: entities,
        FailedPasses: failed));

    if (!offline)
    {
        Console.WriteLine($"  extracted in {seconds:F0}s");
    }
}

Print(scores);

if (reportPath is not null)
{
    var json = JsonSerializer.Serialize(scores, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(reportPath, json);
    Console.WriteLine($"\nreport written to {reportPath}");
}

// A run where nothing was recalled is a failed run, so CI can gate on it.
return scores.Count > 0 && scores.All(s => s.Recall > 0) ? 0 : 1;

string? Arg(string flag)
{
    var index = Array.IndexOf(args, flag);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

async Task<string> IngestAsync(string path)
{
    using var form = new MultipartFormDataContent();
    using var file = new StreamContent(File.OpenRead(path));
    form.Add(file, "file", Path.GetFileName(path));
    form.Add(new StringContent("eval"), "sourceSystem");
    // Always reprocess: the point is to measure the pipeline as it is now.
    form.Add(new StringContent("true"), "force");

    var response = await http.PostAsync("/api/v1/ingest", form);
    response.EnsureSuccessStatusCode();

    var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    return body["documentId"]!.GetValue<string>();
}

async Task<JsonNode> ExtractAsync(string documentId, string[] only)
{
    var payload = new JsonObject
    {
        ["documentId"] = documentId,
        ["passes"] = new JsonArray(only.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
    };

    var response = await http.PostAsync("/api/v1/extract",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    response.EnsureSuccessStatusCode();

    return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
}

static void Print(List<DocumentScore> scores)
{
    foreach (var doc in scores)
    {
        Console.WriteLine();
        Console.WriteLine($"══ {doc.Document}   schema {(doc.SchemaValid ? "valid" : "INVALID")}   {doc.Seconds:F0}s");
        Console.WriteLine();
        Console.WriteLine($"  {"entity",-16}{"exp",4}{"key",5}{"lbl",5}{"miss",6}{"extra",7}{"recall",9}{"prec",8}{"fields",9}");
        Console.WriteLine($"  {new string('─', 69)}");

        foreach (var e in doc.Entities)
        {
            Console.WriteLine(
                $"  {e.Entity,-16}{e.Expected,4}{e.MatchedByKey,5}{e.MatchedByLabel,5}{e.Missing,6}{e.Extra,7}" +
                $"{e.Recall,9:P0}{e.Precision,8:P0}{e.FieldAccuracy,9:P0}");
        }

        Console.WriteLine($"  {new string('─', 69)}");
        Console.WriteLine(
            $"  {"TOTAL",-16}{doc.Expected,4}{doc.Entities.Sum(e => e.MatchedByKey),5}{doc.MatchedByLabel,5}" +
            $"{doc.Expected - doc.Matched,6}{doc.Extra,7}{doc.Recall,9:P0}{doc.Precision,8:P0}{doc.FieldAccuracy,9:P0}");

        Console.WriteLine();
        Console.WriteLine($"  identifier drift  {doc.IdentifierDrift,6:P0}  " +
                          "(share of matches found only by label, not by id)");

        foreach (var e in doc.Entities.Where(e => e.MissingKeys.Count > 0))
        {
            Console.WriteLine($"  missing in {e.Entity}: {string.Join(", ", e.MissingKeys)}");
        }

        foreach (var e in doc.Entities.Where(e => e.ExtraKeys.Count > 0))
        {
            Console.WriteLine($"  not in golden, {e.Entity}: {string.Join(", ", e.ExtraKeys)}");
        }

        var misses = doc.Entities.SelectMany(e => e.FieldMisses).Take(12).ToList();

        if (misses.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  wrong values:");
            foreach (var m in misses)
            {
                Console.WriteLine($"    {m.Key}.{m.Field}: expected '{m.Expected}', got '{m.Actual ?? "—"}'");
            }
        }
    }
}
