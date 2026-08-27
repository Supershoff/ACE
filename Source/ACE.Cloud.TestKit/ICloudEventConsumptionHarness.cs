using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;

namespace ACE.Cloud.TestKit;

/// <summary>
/// The minimal surface an adapter exposes so
/// <see cref="CloudEventConsumptionInvariantSuite{TPayload}"/> can prove ARCH-007's "the web
/// consumes events idempotently" requirement: redelivering an already-applied Custody Outbox event
/// must be a no-op, and out-of-order delivery must still converge to the highest applied version
/// without ever regressing already-applied state (transaction rule 6: outbox effects are delivered
/// at least once, so consumers must be idempotent).
/// </summary>
public interface ICloudEventConsumptionHarness<TPayload>
{
    /// <summary>Applies one event envelope to the projection under test.</summary>
    Task ApplyAsync(CloudEventEnvelope<TPayload> envelope);

    /// <summary>The highest aggregate version the projection has applied so far, or null if none yet.</summary>
    Task<CloudAggregateVersion?> GetAppliedVersionAsync();

    /// <summary>Builds one event envelope carrying <paramref name="version"/> and <paramref name="payload"/>.</summary>
    CloudEventEnvelope<TPayload> CreateEnvelope(CloudAggregateVersion version, TPayload payload);
}
