using Json.Schema;

namespace LabDoc.Api.Services;

/// <summary>
/// Segura o JSON Schema do contrato, compilado uma unica vez.
///
/// Por que uma classe so para isto: JsonSchema.FromText registra o schema num
/// registro GLOBAL, pela URI do $id dele. Compilar duas vezes lanca
/// "Overwriting registered schemas is not permitted" — ou seja, um servico
/// Scoped que compilasse o schema no construtor funcionaria na primeira request
/// e quebraria na segunda. Sendo Singleton, compila no boot e pronto.
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
            logger.LogWarning("Schema do contrato nao encontrado em {Path}: validacao desligada.", path);
            return;
        }

        Schema = JsonSchema.FromText(File.ReadAllText(path));
        logger.LogInformation("Schema do contrato carregado de {Path}.", path);
    }
}
