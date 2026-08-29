namespace ACE.Cloud.Domain;

/// <summary>One ordered, non-empty group of appraisal lines. <see cref="CloudAppraisalProjector"/> never emits an empty section.</summary>
public sealed record CloudAppraisalSection
{
    public required CloudAppraisalSectionKind Kind { get; init; }

    public required IReadOnlyList<CloudAppraisalLine> Lines { get; init; }

    // A record's compiler-synthesized equality compares IReadOnlyList<T> properties by reference (the
    // static property type has no value equality of its own), which would make two structurally
    // identical panels built from equal snapshots compare as unequal. Overriding both members together
    // gives this type real value equality instead, which the determinism tests (issue #27) depend on.
    public bool Equals(CloudAppraisalSection? other) =>
        other is not null && Kind == other.Kind && Lines.SequenceEqual(other.Lines);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        foreach (var line in Lines)
        {
            hash.Add(line);
        }
        return hash.ToHashCode();
    }
}
