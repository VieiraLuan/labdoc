namespace LabDoc.Eval;

/// <summary>
/// How one entity of the contract is compared.
///
/// <paramref name="KeyFields"/> identify an item: two items with the same key are
/// the same thing. <paramref name="LabelField"/> is the human-readable name, used
/// as a fallback when the model invented a different identifier for the same
/// concept — which is the single most common failure of independent LLM passes.
/// </summary>
/// <param name="Path">Where the array lives: "units", or "parameterLists[].items".</param>
/// <param name="AliasField">
/// The identifier this entity defines. Once a top-level entity is matched by label,
/// the model's name for it is recorded as an alias of the expected one, and nested
/// entities are compared through that translation. Otherwise a consistent renaming
/// — every parameter prefixed KF_ — would read as "nothing was extracted", which
/// measures the naming convention rather than the extraction.
/// </param>
public sealed record EntitySpec(
    string Name,
    string Path,
    string[] KeyFields,
    string? LabelField,
    string? AliasField = null)
{
    public static IReadOnlyList<EntitySpec> All { get; } =
    [
        new("units",          "units",                           ["unitId"],                        "unitDesc",          "unitId"),
        new("parameters",     "parameters",                      ["parameterId"],                   "parameterDesc",     "parameterId"),
        new("limitTypes",     "limitTypes",                      ["limitTypeId"],                   "limitTypeDesc",     "limitTypeId"),
        new("parameterLists", "parameterLists",                  ["parameterListId"],               "parameterListDesc"),
        // Flattened across parents on purpose: a wrong parent id must not wipe out
        // the score of the rows, which is where the real extraction signal lives.
        new("paramListItems", "parameterLists[].items",          ["parameterId", "parameterType"],  null),
        new("specifications", "specifications",                  ["specificationId"],               "specificationDesc"),
        new("paramLimits",    "specifications[].parameterLimits", ["parameterId", "limitTypeId"],   null),
        new("testMethods",    "testMethods",                     ["testMethodId"],                  "testMethodDesc"),
    ];
}
