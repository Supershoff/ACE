namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudAuthBridgeClient"/> substitute, so Backend endpoint tests never need a real Auth Bridge over HTTP.</summary>
internal sealed class FakeCloudAuthBridgeClient : ICloudAuthBridgeClient
{
    public CloudAuthBridgeGrantResult NextGrantResult { get; set; } =
        new(CloudAuthBridgeGrantOutcomeKind.InvalidCredentials, Grant: null);

    public uint? NextAccessLevel { get; set; }

    public int AccessLevelCallCount { get; private set; }

    public Task<CloudAuthBridgeGrantResult> IssueGrantAsync(
        string accountName, string password, string audience, CancellationToken cancellationToken = default) =>
        Task.FromResult(NextGrantResult);

    public Task<uint?> GetFreshAccessLevelAsync(uint accountId, CancellationToken cancellationToken = default)
    {
        AccessLevelCallCount++;
        return Task.FromResult(NextAccessLevel);
    }
}
