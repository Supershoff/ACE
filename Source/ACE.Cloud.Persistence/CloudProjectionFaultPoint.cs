namespace ACE.Cloud.Persistence;

/// <summary>
/// Points at which a projection consumer's test-only poison-event hook (see the internal overloads
/// of <see cref="CloudCustodyProjectionConsumer"/>/<see cref="CloudIdentityProjectionConsumer"/>) may
/// simulate a failure while applying one specific outbox event, the same "named fault point plus
/// test-only injector" shape <see cref="CloudBoundaryFaultPoint"/> already uses for crash-safety
/// tests. Production callers never supply an injector.
/// </summary>
public enum CloudProjectionFaultPoint
{
    /// <summary>Immediately before the event is applied to its projection row.</summary>
    BeforeApply,
}
