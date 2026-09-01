namespace ACE.Cloud.Domain;

/// <summary>
/// A personal Sharing Grant (SHARE-001..004): one owner's permission assignment to a grantee's
/// resolved, immutable Main/Linked ownership group. <see cref="GranteeAccountId"/> is resolved
/// exactly once, from the owner's typed current character-name input, to the grantee's effective
/// Main Account ID (SHARE-001: "addressed through a current character but stored against the
/// resolved immutable Main Account ID") -- nothing here ever re-resolves that lookup, so a later
/// rename or deletion of the character originally typed does not move or break the grant
/// (CONTEXT.md: "survives character deletion or rename").
///
/// Immutable; <see cref="CloudSharingGrantPolicy.EvaluateSet"/> returns a new instance carrying the
/// next <see cref="Version"/> rather than mutating this one (ARCH-006, transaction rule 3), mirroring
/// <see cref="CloudTransferOffer"/>'s own shape.
/// </summary>
public sealed class CloudSharingGrant
{
    public CloudSharingGrantId Id { get; }

    public CloudAccountId OwnerAccountId { get; }

    public CloudAccountId GranteeAccountId { get; }

    public CloudSharingGrantLevel Level { get; }

    public CloudAggregateVersion Version { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public CloudSharingGrant(
        CloudSharingGrantId id,
        CloudAccountId ownerAccountId,
        CloudAccountId granteeAccountId,
        CloudSharingGrantLevel level,
        DateTimeOffset createdAtUtc)
        : this(id, ownerAccountId, granteeAccountId, level, CloudAggregateVersion.Initial, createdAtUtc, createdAtUtc)
    {
    }

    private CloudSharingGrant(
        CloudSharingGrantId id,
        CloudAccountId ownerAccountId,
        CloudAccountId granteeAccountId,
        CloudSharingGrantLevel level,
        CloudAggregateVersion version,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(ownerAccountId);
        ArgumentNullException.ThrowIfNull(granteeAccountId);
        ArgumentNullException.ThrowIfNull(version);

        if (granteeAccountId == ownerAccountId)
        {
            throw new ArgumentException("A Sharing Grant cannot name its owner as its own grantee.", nameof(granteeAccountId));
        }

        Id = id;
        OwnerAccountId = ownerAccountId;
        GranteeAccountId = granteeAccountId;
        Level = level;
        Version = version;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    /// <summary>
    /// Applies a new explicit level (including <see cref="CloudSharingGrantLevel.None"/>, an explicit
    /// denial -- SHARE-004). A same-value re-send is still a no-op that does not bump
    /// <see cref="Version"/> (mirrors <see cref="CloudCustodianConfigurationPolicy"/>'s established
    /// "same-value toggle" discipline), so idempotent re-application of an unchanged grant never
    /// invalidates anything that keyed off its version.
    /// </summary>
    internal CloudSharingGrant WithLevel(CloudSharingGrantLevel level, DateTimeOffset updatedAtUtc) =>
        level == Level
            ? this
            : new(Id, OwnerAccountId, GranteeAccountId, level, Version.Next(), CreatedAtUtc, updatedAtUtc);
}
