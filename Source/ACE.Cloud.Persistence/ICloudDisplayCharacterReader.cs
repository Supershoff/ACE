namespace ACE.Cloud.Persistence;

/// <summary>
/// The read half of <see cref="CloudDisplayCharacterGateway"/> issue #33's account identity HTTP
/// endpoint needs (AUTH-003). Interface-extracted for the same reason as
/// <see cref="ICloudAccountOwnershipResolver"/>: so <c>ACE.Cloud.Backend.Tests</c> can substitute an
/// in-memory fake instead of standing up a real MariaDB-backed <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudDisplayCharacterReader
{
    Task<CloudDisplayCharacterSelection?> GetCurrentSelectionAsync(Guid ownershipGroupId, CancellationToken cancellationToken = default);
}
