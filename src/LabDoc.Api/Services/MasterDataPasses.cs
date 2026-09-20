using LabDoc.Api.Models;

namespace LabDoc.Api.Services;

/// <summary>
/// The 'map' half of the extraction, declared as data.
///
/// Each entry says WHAT to extract (Instruction), in WHAT SHAPE (JsonSchema) and
/// FROM WHERE (Sections). Adding an entity to the contract means adding an item
/// to this list — there is no new code to write in the extractor.
///
/// The order follows the dependency order of the target model (units ->
/// parameters -> limit types -> parameter lists -> specifications -> test
/// methods), so the payload reads in the same sequence a consumer would apply it.
/// </summary>
public static class MasterDataPasses
{
    // Attached to every extracted item: where in the document it came from.
    // Without provenance a wrong payload is indistinguishable from a right one.
    private const string ProvenanceBlock = """
        "_source": {
          "type": "object",
          "properties": {
            "section": { "type": "string" },
            "row": { "type": "string" },
            "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
          }
        }
        """;

    public static IReadOnlyList<ExtractionPass> All { get; } =
    [
        new ExtractionPass(
            Name: "source",
            Instruction:
                "Extract the header metadata of the Work Instruction: document identifier " +
                "(e.g. WI-CHM-001), version, full title and effective date in ISO format (YYYY-MM-DD). " +
                "If a field is not written in the document, omit it rather than inventing it.",
            JsonSchema: $$"""
                {
                  "type": "object",
                  "properties": {
                    "documentId":      { "type": "string" },
                    "documentVersion": { "type": "string" },
                    "documentTitle":   { "type": "string" },
                    "effectiveDate":   { "type": "string" },
                    "confidence":      { "type": "number", "minimum": 0, "maximum": 1 }
                  },
                  "required": ["documentId", "documentTitle"]
                }
                """,
            Sections: []),

        new ExtractionPass(
            Name: "units",
            Instruction:
                "List every distinct unit of measure appearing in the parameter and limit tables " +
                "of THIS document. One entry per unit, no duplicates. " +
                "Reproduce each unit exactly as the document writes it. " +
                "Never convert, normalise or expand a unit, and never add a unit the document " +
                "does not contain.",
            JsonSchema: $$"""
                {
                  "type": "object",
                  "properties": {
                    "units": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "unitId":     { "type": "string" },
                          "unitDesc":   { "type": "string" },
                          "activeFlag": { "type": "string", "enum": ["Y", "N"] },
                          {{ProvenanceBlock}}
                        },
                        "required": ["unitId", "unitDesc"]
                      }
                    }
                  },
                  "required": ["units"]
                }
                """,
            Sections: ["8", "11"],
            IdField: "unitId"),

        new ExtractionPass(
            Name: "parameters",
            Instruction:
                "List the distinct measured or calculated quantities of the method. Use UPPERCASE " +
                "identifiers with underscores, derived from the parameter name. " +
                "CRITICAL RULE: mean, standard deviation, %RSD, minimum, maximum and range of " +
                "replicates are NOT new parameters. They are the SAME parameter with a different " +
                "parameterType, so they must not appear in this list. " +
                "Example: a document with the rows 'Water content' and 'Mean water content' yields " +
                "ONE parameter KF_WATER_PCT — never a separate KF_WATER_MEAN.",
            JsonSchema: $$"""
                {
                  "type": "object",
                  "properties": {
                    "parameters": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "parameterId":   { "type": "string" },
                          "parameterDesc": { "type": "string" },
                          "activeFlag":    { "type": "string", "enum": ["Y", "N"] },
                          {{ProvenanceBlock}}
                        },
                        "required": ["parameterId", "parameterDesc"]
                      }
                    }
                  },
                  "required": ["parameters"]
                }
                """,
            Sections: ["8", "9"],
            IdField: "parameterId"),

        new ExtractionPass(
            Name: "limitTypes",
            Instruction:
                "List the kinds of limit named in the limits table. Release limit, alert limit, " +
                "system suitability and process control are DIFFERENT limit types, not different " +
                "operators of the same limit. 'condition' is the outcome reported when the limit " +
                "is applied (Pass, Fail, Warn). " +
                "limitTypeDesc is a SHORT label of two to four words, never the full sentence " +
                "copied from the document.",
            JsonSchema: $$"""
                {
                  "type": "object",
                  "properties": {
                    "limitTypes": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "limitTypeId":   { "type": "string" },
                          "limitTypeDesc": { "type": "string" },
                          "appliesTo":     { "type": "string", "enum": ["Parameter", "Specification"] },
                          "condition":     { "type": "string" },
                          {{ProvenanceBlock}}
                        },
                        "required": ["limitTypeId", "limitTypeDesc"]
                      }
                    }
                  },
                  "required": ["limitTypes"]
                }
                """,
            Sections: ["11"],
            IdField: "limitTypeId"),

        new ExtractionPass(
            Name: "parameterLists",
            Instruction:
                "Build the data entry template for this document. Produce exactly ONE parameter " +
                "list covering every parameter, never one list per document section. " +
                "One row per parameter of the table. " +
                "parameterType is 'Standard' for the measured value; use 'Average', " +
                "'StandardDeviation' or '%RSD' for replicate statistics, repeating the SAME parameterId. " +
                "dataType: 'N' numeric entered or read from an instrument, 'NC' numeric calculated " +
                "(the formula goes in calcRule), 'T' text, 'R' selection from a list, 'S' lookup on " +
                "another record. userSequence follows the row order of the table, in steps of 10. " +
                "parameterId and displayUnit MUST be taken verbatim from the identifiers already " +
                "extracted, listed below. Never rename them and never invent a new parameterId " +
                "for a replicate statistic.",
            JsonSchema: $$"""
                {
                  "type": "object",
                  "properties": {
                    "parameterLists": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "parameterListId":   { "type": "string" },
                          "versionId":         { "type": "string" },
                          "parameterListDesc": { "type": "string" },
                          "versionStatus":     { "type": "string", "enum": ["P", "A", "C", "E"] },
                          "items": {
                            "type": "array",
                            "items": {
                              "type": "object",
                              "properties": {
                                "parameterId":   { "type": "string" },
                                "parameterType": { "type": "string", "enum": ["Standard", "Average", "StandardDeviation", "%RSD", "Maximum", "Minimum", "Range"] },
                                "dataType":      { "type": "string", "enum": ["N", "NC", "T", "TC", "D", "DC", "R", "S"] },
                                "displayUnit":   { "type": "string" },
                                "displayFormat": { "type": "string" },
                                "numReplicates": { "type": "integer", "minimum": 1 },
                                "mandatoryFlag": { "type": "string", "enum": ["Y", "N"] },
                                "calcRule":      { "type": "string" },
                                "userSequence":  { "type": "integer" },
                                {{ProvenanceBlock}}
                              },
                              "required": ["parameterId", "parameterType", "dataType"]
                            }
                          }
                        },
                        "required": ["parameterListId", "parameterListDesc", "items"]
                      }
                    }
                  },
                  "required": ["parameterLists"]
                }
                """,
            Sections: ["8", "9"],
            DependsOn: ["units", "parameters"],
            IdField: "parameterListId"),

        new ExtractionPass(
            Name: "specifications",
            Instruction:
                "Build the specification from the numeric limits of the limits table. " +
                "A range uses both operators: operator1 '>=' with value1 and operator2 '<=' with value2. " +
                "Reproduce the numbers exactly as written, without rounding or converting units. " +
                "oosGeneratingFlag comes from the 'OOS generating' column: 'Y' when a failure is a " +
                "formal out-of-specification event. If the document has both OOS and non-OOS limits, " +
                "that is TWO specifications, because the flag belongs to the specification and not " +
                "to the individual limit. " +
                "limitTypeId MUST be one of the limit type identifiers already extracted, listed " +
                "below — it names WHICH KIND of limit this is, and is never an operator: " +
                "the comparison itself belongs in operator1 and operator2. " +
                "parameterId and parameterType MUST also be taken verbatim from the identifiers below.",
            JsonSchema: $$"""
                {
                  "type": "object",
                  "properties": {
                    "specifications": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "specificationId":   { "type": "string" },
                          "versionId":         { "type": "string" },
                          "specificationDesc": { "type": "string" },
                          "oosGeneratingFlag": { "type": "string", "enum": ["Y", "N"] },
                          "versionStatus":     { "type": "string", "enum": ["P", "A", "C", "E"] },
                          "useType":           { "type": "string", "enum": ["Customer", "Regulatory", "Internal", "Other"] },
                          "parameterLimits": {
                            "type": "array",
                            "items": {
                              "type": "object",
                              "properties": {
                                "parameterId":   { "type": "string" },
                                "parameterType": { "type": "string" },
                                "limitTypeId":   { "type": "string" },
                                "operator1":     { "type": "string", "enum": ["<", "<=", "=", ">=", ">", "In", "Not In", "Out"] },
                                "value1":        { "type": "string" },
                                "operator2":     { "type": "string", "enum": ["<", "<=", "=", ">=", ">", "In", "Not In", "Out"] },
                                "value2":        { "type": "string" },
                                {{ProvenanceBlock}}
                              },
                              "required": ["parameterId", "limitTypeId", "operator1", "value1"]
                            }
                          }
                        },
                        "required": ["specificationId", "specificationDesc", "parameterLimits"]
                      }
                    }
                  },
                  "required": ["specifications"]
                }
                """,
            Sections: ["10", "11"],
            DependsOn: ["parameters", "limitTypes", "parameterLists"],
            IdField: "specificationId"),

        new ExtractionPass(
            Name: "testMethods",
            Instruction:
                "Build the test method from the document header: title, owning department, " +
                "applicable sample type, whether the test is destructive, the sample quantity " +
                "required and the nominal turnaround. Extract only what is written.",
            JsonSchema: $$"""
                {
                  "type": "object",
                  "properties": {
                    "testMethods": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "testMethodId":           { "type": "string" },
                          "versionId":              { "type": "string" },
                          "testMethodDesc":         { "type": "string" },
                          "departmentId":           { "type": "string" },
                          "applicableSampleTypeId": { "type": "string" },
                          "destructiveTestFlag":    { "type": "string", "enum": ["Y", "N"] },
                          "quantity":               { "type": "string" },
                          "quantityUnit":           { "type": "string" },
                          {{ProvenanceBlock}}
                        },
                        "required": ["testMethodId", "testMethodDesc"]
                      }
                    }
                  },
                  "required": ["testMethods"]
                }
                """,
            Sections: []),

        new ExtractionPass(
            Name: "references",
            Instruction:
                "List the master data the document MENTIONS but this payload does not create: " +
                "sample types, instrument types, reagent types and departments. These belong to " +
                "other domains and are normally already configured. " +
                "policy 'require' means it must exist beforehand; 'create' means it may be created.",
            JsonSchema: $$"""
                {
                  "type": "object",
                  "properties": {
                    "references": {
                      "type": "object",
                      "properties": {
                        "sampleTypes":     { "type": "array", "items": { "type": "object", "properties": { "id": { "type": "string" }, "label": { "type": "string" }, "policy": { "type": "string", "enum": ["require", "create", "ignore"] } }, "required": ["id"] } },
                        "instrumentTypes": { "type": "array", "items": { "type": "object", "properties": { "id": { "type": "string" }, "label": { "type": "string" }, "policy": { "type": "string", "enum": ["require", "create", "ignore"] } }, "required": ["id"] } },
                        "reagentTypes":    { "type": "array", "items": { "type": "object", "properties": { "id": { "type": "string" }, "label": { "type": "string" }, "policy": { "type": "string", "enum": ["require", "create", "ignore"] } }, "required": ["id"] } },
                        "departments":     { "type": "array", "items": { "type": "object", "properties": { "id": { "type": "string" }, "label": { "type": "string" }, "policy": { "type": "string", "enum": ["require", "create", "ignore"] } }, "required": ["id"] } }
                      }
                    }
                  },
                  "required": ["references"]
                }
                """,
            Sections: ["4", "5", "6"]),

        new ExtractionPass(
            Name: "unresolved",
            Instruction:
                "This is the human review queue, and the escape valve against hallucination: " +
                "list what the document describes as relevant but that does NOT fit the contract's " +
                "fields. Typically: values that come from another record (a product-specific limit, " +
                "a label claim), conditional specifications stated in prose, stage logic, and " +
                "decisions that depend on the product rather than on the method. " +
                "Prefer recording something here over inventing a field: nothing listed here " +
                "becomes master data.",
            JsonSchema: $$"""
                {
                  "type": "object",
                  "properties": {
                    "unresolved": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "section":    { "type": "string" },
                          "text":       { "type": "string" },
                          "reason":     { "type": "string" },
                          "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
                        },
                        "required": ["section", "text", "reason"]
                      }
                    }
                  },
                  "required": ["unresolved"]
                }
                """,
            Sections: []),
    ];
}
