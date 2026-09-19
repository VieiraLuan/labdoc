using System.ComponentModel.DataAnnotations;

namespace LabDoc.Api.DTOs.Request;

public sealed record AskRequest(
    [property: Required(AllowEmptyStrings = false)]
    [property: StringLength(1000, MinimumLength = 3)]
    string Question,

    [property: Range(1, 20)]
    int? TopK = null);
