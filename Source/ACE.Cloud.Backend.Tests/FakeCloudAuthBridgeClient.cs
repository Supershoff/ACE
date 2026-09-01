namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudAuthBridgeClient"/> substitute, so Backend endpoint tests never need a real Auth Bridge over HTTP.</summary>
internal sealed class FakeCloudAuthBridgeClient : ICloudAuthBridgeClient
{
    public CloudAuthBridgeGrantResult NextGrantResult { get; set; } =
        new(CloudAuthBridgeGrantOutcomeKind.InvalidCredentials, Grant: null);

    public uint? NextAccessLevel { get; set; }

    /// <summary>Per-account overrides for <see cref="GetFreshAccessLevelAsync"/>, so a test can make one account ID (e.g. an admin-typed destination) report as nonexistent (null) while <see cref="NextAccessLevel"/> still answers for every other account, such as the caller's own session.</summary>
    public Dictionary<uint, uint?> AccessLevelByAccountId { get; } = new();

    public int AccessLevelCallCount { get; private set; }

    public Task<CloudAuthBridgeGrantResult> IssueGrantAsync(
        string accountName, string password, string audience, CancellationToken cancellationToken = default) =>
        Task.FromResult(NextGrantResult);

    public Task<uint?> GetFreshAccessLevelAsync(uint accountId, CancellationToken cancellationToken = default)
    {
        AccessLevelCallCount++;
        return Task.FromResult(AccessLevelByAccountId.TryGetValue(accountId, out var overridden) ? overridden : NextAccessLevel);
    }
}
