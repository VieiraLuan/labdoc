namespace LabDoc.Api.Interfaces;

public interface IRagChunkService
{
    IReadOnlyList<string> Split(string text);
}
