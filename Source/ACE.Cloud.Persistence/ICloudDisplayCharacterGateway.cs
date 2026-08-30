using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Display Character capabilities issue #33's HTTP surface needs from
/// <see cref="CloudDisplayCharacterGateway"/> (AUTH-003), interface-extracted so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake, mirroring
/// <see cref="ICloudAccountOwnershipResolver"/>'s existing precedent.
/// </summary>
public interface ICloudDisplayCharacterGateway
{
    Task<CloudDisplayCharacterSelectionResult> ReselectAsync(
        string shardId,
        Guid ownershipGroupId,
        IReadOnlyList<CloudDisplayCharacterCandidate> candidates,
        CloudDisplayCharacterSelectionReason reason,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    Task<CloudDisplayCharacterSelection?> GetCurrentSelectionAsync(Guid ownershipGroupId, CancellationToken cancellationToken = default);
}
