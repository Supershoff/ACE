namespace ACE.Cloud.Domain;

public sealed record CloudAppraisalGoldenComparisonReport
{
    public required IReadOnlyList<CloudAppraisalGoldenComparisonResult> Results { get; init; }

    public bool AllMatch => Results.All(result => result.Outcome == CloudAppraisalGoldenComparisonOutcome.Match);

    public bool Equals(CloudAppraisalGoldenComparisonReport? other) =>
        other is not null && Results.SequenceEqual(other.Results);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var result in Results)
        {
            hash.Add(result);
        }
        return hash.ToHashCode();
    }
}
