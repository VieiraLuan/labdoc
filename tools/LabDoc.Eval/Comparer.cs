using System.Text.Json.Nodes;

namespace LabDoc.Eval;

/// <summary>
/// Compares an extracted payload against a hand-written expected one.
///
/// Matching happens in two rounds, and the gap between them is itself a result:
///
///   1. by key   — the model produced the same identifier for the same thing
///   2. by label — the concept is there under a different identifier
///
/// Round 2 exists because independent LLM passes share no vocabulary, so one pass
/// emits CELL_DRIFT and another KF_CELL_DRIFT. Scoring only by key would report
/// that as a total miss and hide whether the information was extracted at all.
/// </summary>
public static class Comparer
{
    // Provenance, not content: the model's opinion of where a value came from is
    // not what we are grading here.
    private static readonly string[] IgnoredFields = ["_source"];

    // Compared as literal text. Numeric normalisation is right for values —
    // "0.100" and "0.1" are the same number — but wrong for a display mask,
    // where "0.0000" would collapse to "0" and stop meaning anything.
    private static readonly string[] LiteralFields = ["displayFormat"];

    public static EntityScore Compare(
        EntitySpec spec,
        JsonNode? expectedRoot,
        JsonNode? actualRoot,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? aliases = null)
    {
        var expected = Collect(expectedRoot, spec.Path);
        var actual = Collect(actualRoot, spec.Path);

        var remaining = actual.ToList();

        var matchedByKey = 0;
        var matchedByLabel = 0;
        var fieldsCompared = 0;
        var fieldsCorrect = 0;

        var missing = new List<string>();
        var fieldMisses = new List<FieldMiss>();
        var discovered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var want in expected)
        {
            var wantKey = Key(want, spec.KeyFields);

            var hit = remaining.FirstOrDefault(a =>
                string.Equals(Key(a, spec.KeyFields, aliases), wantKey, StringComparison.OrdinalIgnoreCase));

            if (hit is not null)
            {
                matchedByKey++;
            }
            else if (spec.LabelField is not null)
            {
                hit = remaining.FirstOrDefault(a => LabelsMatch(want, a, spec.LabelField));

                if (hit is not null)
                {
                    matchedByLabel++;
                }
            }

            // Whatever the model called it now stands for the expected identifier
            // in every entity compared after this one.
            if (hit is not null && spec.AliasField is not null)
            {
                var theirs = Normalize(hit[spec.AliasField]);
                var ours = Normalize(want[spec.AliasField]);

                if (theirs is not null && ours is not null)
                {
                    discovered[theirs] = ours;
                }
            }

            if (hit is null)
            {
                missing.Add(wantKey);
                continue;
            }

            remaining.Remove(hit);
            ScoreFields(want, hit, wantKey, ref fieldsCompared, ref fieldsCorrect, fieldMisses);
        }

        return new EntityScore(
            Entity: spec.Name,
            Expected: expected.Count,
            MatchedByKey: matchedByKey,
            MatchedByLabel: matchedByLabel,
            Missing: missing.Count,
            Extra: remaining.Count,
            FieldsCompared: fieldsCompared,
            FieldsCorrect: fieldsCorrect,
            MissingKeys: missing,
            ExtraKeys: remaining.Select(a => Key(a, spec.KeyFields, aliases)).ToList(),
            FieldMisses: fieldMisses,
            Aliases: discovered);
    }

    /// <summary>
    /// Only fields the expected payload states are graded. Anything the golden
    /// leaves unsaid is not an error — it is simply not specified.
    /// </summary>
    private static void ScoreFields(
        JsonObject want,
        JsonObject got,
        string key,
        ref int compared,
        ref int correct,
        List<FieldMiss> misses)
    {
        foreach (var property in want)
        {
            if (IgnoredFields.Contains(property.Key) || property.Value is JsonArray or JsonObject)
            {
                continue;
            }

            compared++;

            var literal = LiteralFields.Contains(property.Key);

            var expectedValue = Normalize(property.Value, literal);
            var actualValue = Normalize(got[property.Key], literal);

            if (expectedValue == actualValue)
            {
                correct++;
            }
            else
            {
                misses.Add(new FieldMiss(key, property.Key, expectedValue, actualValue));
            }
        }
    }

    /// <summary>
    /// Walks "units" or "parameterLists[].items", flattening the nested case across
    /// every parent.
    /// </summary>
    private static List<JsonObject> Collect(JsonNode? root, string path)
    {
        var results = new List<JsonObject>();

        if (root is null)
        {
            return results;
        }

        var segments = path.Split('.');
        var current = new List<JsonNode> { root };

        foreach (var segment in segments)
        {
            var name = segment.EndsWith("[]", StringComparison.Ordinal) ? segment[..^2] : segment;
            var next = new List<JsonNode>();

            foreach (var node in current)
            {
                if (node is JsonObject obj && obj[name] is JsonArray array)
                {
                    next.AddRange(array.OfType<JsonNode>());
                }
            }

            current = next;
        }

        results.AddRange(current.OfType<JsonObject>());

        return results;
    }

    private static string Key(
        JsonObject item,
        string[] fields,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? aliases = null)
        => string.Join('|', fields.Select(f => Translate(f, Normalize(item[f]) ?? "", aliases)));

    private static string Translate(
        string field,
        string value,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? aliases)
        => aliases is not null
           && aliases.TryGetValue(field, out var map)
           && map.TryGetValue(value, out var canonical)
            ? canonical
            : value;

    /// <summary>
    /// Two labels are the same concept when their significant words overlap enough.
    /// "Cell drift" and "Cell drift (ug/min)" are the same row; "Cell drift" and
    /// "Sample mass" are not.
    /// </summary>
    private static bool LabelsMatch(JsonObject want, JsonObject got, string labelField)
    {
        var a = Words(Normalize(want[labelField]));
        var b = Words(Normalize(got[labelField]));

        if (a.Count == 0 || b.Count == 0)
        {
            return false;
        }

        var shared = a.Intersect(b).Count();

        return shared >= Math.Ceiling(Math.Min(a.Count, b.Count) * 0.6);
    }

    private static HashSet<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split([' ', '-', '_', '/', '(', ')', ',', '.'], StringSplitOptions.RemoveEmptyEntries)
                  .Where(w => w.Length > 2)
                  .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// "3" and 3 are the same answer; "0.100" and "0.1" are the same number.
    /// Formatting is not what is being graded.
    /// </summary>
    private static string? Normalize(JsonNode? node, bool literal = false)
    {
        if (node is null)
        {
            return null;
        }

        var raw = node.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.String => node.GetValue<string>(),
            _ => node.ToJsonString().Trim('"'),
        };

        raw = raw.Trim();

        return !literal
            && double.TryParse(raw, System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture)
            : raw.ToLowerInvariant();
    }
}
