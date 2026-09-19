using Json.Schema;

namespace LabDoc.Api.Services;

/// <summary>
/// Holds the contract's JSON Schema, compiled exactly once.
///
/// Why this deserves its own class: JsonSchema.FromText registers the schema in a
/// GLOBAL registry, keyed by its $id URI. Compiling it twice throws
/// "Overwriting registered schemas is not permitted" — so a Scoped service that
/// compiled the schema in its constructor would work on the first request and
/// break on the second. As a Singleton it compiles at boot and that is that.
/// </summary>
public sealed class MasterDataSchema
{
    private const string SchemaFile = "Schemas/lab-masterdata.schema.json";

    public JsonSchema? Schema { get; }

    public MasterDataSchema(IHostEnvironment environment, ILogger<MasterDataSchema> logger)
    {
        var path = Path.Combine(environment.ContentRootPath, SchemaFile);

        if (!File.Exists(path))
        {
            logger.LogWarning("Contract schema not found at {Path}: validation is disabled.", path);
            return;
        }

        Schema = JsonSchema.FromText(File.ReadAllText(path));
        logger.LogInformation("Contract schema loaded from {Path}.", path);
    }
}
