using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Gathers Display Character candidates (AUTH-003) for a set of ACE account IDs from
/// <see cref="CloudCharacterIdentityReadProjection"/> -- the "versioned/refreshed from ACE" cache
/// CONTEXT.md sanctions -- rather than reading <c>ace_shard.character</c> directly, which the
/// narrowly privileged companion web identity (ARCH-004) must never do (see
/// <see cref="ACE.Cloud.Backend.CloudBackendOptions.CloudConnectionString"/>'s doc comment).
/// </summary>
public interface ICloudCharacterIdentityReader
{
    /// <summary>
    /// Every currently-cached character belonging to one of <paramref name="accountIds"/>, projected
    /// into <see cref="CloudDisplayCharacterCandidate"/>. Known gap, documented rather than silently
    /// dropped: this cache does not yet carry an explicit "still exists" marker distinct from a
    /// rename (see <see cref="CloudCharacterIdentityReadProjection.TryApply"/>'s doc comment), so a
    /// deleted character's last-known snapshot can still appear here until that projection gains a
    /// deletion marker in a future issue. A caller that needs a guaranteed-current roster (as opposed
    /// to "the group's roster changed and needs recomputing") must not treat this as authoritative.
    /// </summary>
    Task<IReadOnlyList<CloudDisplayCharacterCandidate>> GetCandidatesAsync(
        string shardId, IReadOnlyCollection<uint> accountIds, CancellationToken cancellationToken = default);
}
