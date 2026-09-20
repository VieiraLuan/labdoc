namespace LabDoc.Eval;

public sealed record FieldMiss(string Key, string Field, string? Expected, string? Actual);

public sealed record EntityScore(
    string Entity,
    int Expected,
    int MatchedByKey,
    int MatchedByLabel,
    int Missing,
    int Extra,
    int FieldsCompared,
    int FieldsCorrect,
    IReadOnlyList<string> MissingKeys,
    IReadOnlyList<string> ExtraKeys,
    IReadOnlyList<FieldMiss> FieldMisses,
    // What the model called each thing, mapped to what the golden calls it.
    IReadOnlyDictionary<string, string> Aliases)
{
    public int Matched => MatchedByKey + MatchedByLabel;

    // Recall is the metric that matters here: a parameter silently dropped is
    // worse than a parameter with a wrong value, because the payload still looks fine.
    public double Recall => Expected == 0 ? 1 : (double)Matched / Expected;

    public double Precision => Matched + Extra == 0 ? 1 : (double)Matched / (Matched + Extra);

    public double FieldAccuracy => FieldsCompared == 0 ? 1 : (double)FieldsCorrect / FieldsCompared;

    // How much of the recall depended on falling back to the label: the share of
    // matches where the model found the concept but named it differently.
    public double IdentifierDrift => Matched == 0 ? 0 : (double)MatchedByLabel / Matched;
}

public sealed record DocumentScore(
    string Document,
    bool SchemaValid,
    double Seconds,
    IReadOnlyList<EntityScore> Entities,
    IReadOnlyList<string> FailedPasses)
{
    public int Expected => Entities.Sum(e => e.Expected);
    public int Matched => Entities.Sum(e => e.Matched);
    public int Extra => Entities.Sum(e => e.Extra);
    public int FieldsCompared => Entities.Sum(e => e.FieldsCompared);
    public int FieldsCorrect => Entities.Sum(e => e.FieldsCorrect);
    public int MatchedByLabel => Entities.Sum(e => e.MatchedByLabel);

    public double Recall => Expected == 0 ? 1 : (double)Matched / Expected;
    public double Precision => Matched + Extra == 0 ? 1 : (double)Matched / (Matched + Extra);
    public double FieldAccuracy => FieldsCompared == 0 ? 1 : (double)FieldsCorrect / FieldsCompared;
    public double IdentifierDrift => Matched == 0 ? 0 : (double)MatchedByLabel / Matched;
}
