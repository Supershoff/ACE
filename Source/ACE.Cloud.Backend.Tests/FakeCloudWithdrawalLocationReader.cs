using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudWithdrawalLocationReader"/> substitute for endpoint tests.</summary>
internal sealed class FakeCloudWithdrawalLocationReader : ICloudWithdrawalLocationReader
{
    public CloudWithdrawalLocationConfiguration Current { get; set; } = CloudWithdrawalLocationConfiguration.Default();

    public Task<CloudWithdrawalLocationConfiguration> GetCurrentAsync(string shardId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);
}
