namespace LabDoc.Api.Models;

/// <summary>
/// Uma passada do 'map': uma chamada ao LLM que extrai UMA entidade do contrato.
///
/// A ideia central da extracao e nao pedir o payload inteiro de uma vez. Cada
/// passada leva so o schema da propria entidade e so as secoes que interessam,
/// entao cada chamada e pequena, focada e verificavel isoladamente.
/// </summary>
/// <param name="Name">Chave da entidade no envelope: "units", "parameters"...</param>
/// <param name="Instruction">O que extrair, em linguagem natural.</param>
/// <param name="JsonSchema">Fragmento de JSON Schema que restringe a saida.</param>
/// <param name="Sections">Secoes da WI que alimentam o contexto. Vazio = documento inteiro.</param>
public sealed record ExtractionPass(
    string Name,
    string Instruction,
    string JsonSchema,
    IReadOnlyList<string> Sections);
