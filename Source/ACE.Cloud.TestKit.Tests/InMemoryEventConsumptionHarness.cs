using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>
/// A minimal, storage-agnostic reference implementation of
/// <see cref="ICloudEventConsumptionHarness{TPayload}"/>: a projection that applies an envelope
/// only when its version is strictly higher than the highest version already applied, which is the
/// idempotent, order-tolerant consumption rule ARCH-007 requires of a real outbox consumer.
/// </summary>
public sealed class InMemoryEventConsumptionHarness : ICloudEventConsumptionHarness<string>
{
    private static readonly CloudShardId ShardId = new("us1");

    private readonly object _gate = new();
    private CloudAggregateVersion? _appliedVersion;

    public Task ApplyAsync(CloudEventEnvelope<string> envelope)
    {
        lock (_gate)
        {
            if (_appliedVersion is null || envelope.Version > _appliedVersion)
            {
                _appliedVersion = envelope.Version;
            }
        }

        return Task.CompletedTask;
    }

    public Task<CloudAggregateVersion?> GetAppliedVersionAsync()
    {
        lock (_gate)
        {
            return Task.FromResult(_appliedVersion);
        }
    }

    public CloudEventEnvelope<string> CreateEnvelope(CloudAggregateVersion version, string payload) =>
        new(ShardId, version, new CloudIdempotencyKey(Guid.NewGuid()), DateTimeOffset.UtcNow, payload);
}
