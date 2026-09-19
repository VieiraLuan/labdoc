using LabDoc.Api.Models;

namespace LabDoc.Api.Interfaces;

public interface IWorkInstructionParser
{
    IReadOnlyList<DocumentSection> Parse(string text);
}
