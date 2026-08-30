using ACE.Cloud.Hosting;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudServiceAvailabilityReader"/> substitute, defaulting to Operational.</summary>
internal sealed class FakeCloudServiceAvailabilityReader : ICloudServiceAvailabilityReader
{
    public CloudServiceAvailabilityMode Mode { get; set; } = CloudServiceAvailabilityMode.Operational;

    public Task<CloudServiceAvailabilityMode> GetCurrentModeAsync(CancellationToken cancellationToken = default) => Task.FromResult(Mode);
}
