namespace ACE.Cloud.Domain;

/// <summary>
/// One resolved place a Cloud Custodian should currently be spawned (DEP-007, ADM-003): a stable
/// identity plus the exact position it currently resolves to.
/// </summary>
public sealed record CloudCustodianLocation(CloudCustodianLocationKey Key, CloudCustodianPosition Position)
{
    public CloudCustodianLocationKey Key { get; init; } = Key ?? throw new ArgumentNullException(nameof(Key));

    public CloudCustodianPosition Position { get; init; } = Position ?? throw new ArgumentNullException(nameof(Position));
}

/// <summary>
/// One Mansion-tier housing plot's stable identity and current world position, as ACE.Server
/// resolves it from the operator's own live ace_world content (there is no fixed, shippable list of
/// "every mansion" -- see <see cref="CloudCustodianLocationResolver"/>). <paramref name="MansionGuid"/>
/// is the plot's native ace_world <c>LandblockInstance.Guid</c> (an ACE object GUID), which stays
/// stable across server restarts because it is static world content, not player-owned state.
/// </summary>
public sealed record CloudCustodianMansionLocation(uint MansionGuid, CloudCustodianPosition Position)
{
    public CloudCustodianPosition Position { get; init; } = Position ?? throw new ArgumentNullException(nameof(Position));
}
