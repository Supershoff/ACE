namespace ACE.Cloud.Domain;

/// <summary>
/// Global Cloud Maintenance (ADM-004): an administrator-controlled read-only safety state that
/// pauses every Cloud mutation and all expiry clocks without cancelling or unlocking assets. One row
/// per deployment (ARCH-001), matching <see cref="CloudMutationGateState"/>'s own doc comment, which
/// named this the "full administrative aggregate" earlier Cloud Transaction Authority commands
/// deliberately deferred.
/// </summary>
public sealed record CloudGlobalMaintenanceState(
    bool IsFrozen,
    string? Reason,
    DateTime? EnteredAtUtc,
    uint? EnteredByAccountId,
    CloudAggregateVersion Version)
{
    public CloudAggregateVersion Version { get; init; } = Version ?? throw new ArgumentNullException(nameof(Version));

    /// <summary>Out-of-the-box state: mutations are open, no freeze in effect.</summary>
    public static CloudGlobalMaintenanceState Default() =>
        new(IsFrozen: false, Reason: null, EnteredAtUtc: null, EnteredByAccountId: null, CloudAggregateVersion.Initial);
}
