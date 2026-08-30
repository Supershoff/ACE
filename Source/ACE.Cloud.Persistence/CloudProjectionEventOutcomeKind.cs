namespace ACE.Cloud.Persistence;

/// <summary>What happened when a projection consumer tried to apply one outbox event.</summary>
public enum CloudProjectionEventOutcomeKind
{
    /// <summary>The event was newer than what the projection had applied and updated it.</summary>
    Applied,

    /// <summary>A duplicate or stale/out-of-order delivery; correctly ignored (<see cref="ACE.Cloud.Domain.CloudProjectionSequenceGuard"/>).</summary>
    SkippedAsStale,

    /// <summary>A poison event: recorded to <see cref="CloudProjectionDeadLetter"/> and skipped so later events are not blocked.</summary>
    DeadLettered,
}
