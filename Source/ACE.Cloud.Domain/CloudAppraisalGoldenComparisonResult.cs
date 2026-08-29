namespace ACE.Cloud.Domain;

/// <summary>One fixture's deterministic, machine-readable comparison result (acceptance criterion: "emits deterministic machine-readable results for execution in #28").</summary>
public sealed record CloudAppraisalGoldenComparisonResult
{
    public required string FixtureName { get; init; }

    public required CloudAppraisalGoldenComparisonOutcome Outcome { get; init; }

    public IReadOnlyList<string> Differences { get; init; } = [];

    public bool Equals(CloudAppraisalGoldenComparisonResult? other) =>
        other is not null && FixtureName == other.FixtureName && Outcome == other.Outcome && Differences.SequenceEqual(other.Differences);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FixtureName);
        hash.Add(Outcome);
        foreach (var difference in Differences)
        {
            hash.Add(difference);
        }
        return hash.ToHashCode();
    }
}
