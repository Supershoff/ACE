namespace ACE.Cloud.Domain;

/// <summary>
/// The versioned Full Cloud Appraisal presentation contract (UI-004): a character-independent,
/// visually faithful reconstruction of a successful ACE appraisal's player-facing content, shared
/// as-is by backend tests and the later React renderer (Green: "shared snapshot-to-presentation model").
/// <see cref="ContractVersion"/> lets a future breaking change to this shape be introduced without
/// silently reinterpreting an already-persisted or already-cached panel.
/// </summary>
public sealed record CloudAppraisalPanel
{
    public const int CurrentContractVersion = 1;

    public int ContractVersion { get; init; } = CurrentContractVersion;

    public required string ItemName { get; init; }

    public required IReadOnlyList<CloudAppraisalSection> Sections { get; init; }

    // See CloudAppraisalSection's identical override for why this is necessary: the compiler-synthesized
    // record equality would otherwise compare Sections by reference.
    public bool Equals(CloudAppraisalPanel? other) =>
        other is not null && ContractVersion == other.ContractVersion && ItemName == other.ItemName && Sections.SequenceEqual(other.Sections);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ContractVersion);
        hash.Add(ItemName);
        foreach (var section in Sections)
        {
            hash.Add(section);
        }
        return hash.ToHashCode();
    }
}
