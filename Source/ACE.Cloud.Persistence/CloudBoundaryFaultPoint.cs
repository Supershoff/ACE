namespace ACE.Cloud.Persistence;

/// <summary>
/// Every boundary at which <see cref="CloudCustodyBoundary"/> invokes an internal, test-only fault
/// injector; production callers never supply one (see the internal overloads' doc comments). Most
/// values are named in issue #4's Red section, where a crash-safety test injects a simulated crash at
/// each point. <see cref="AfterLocks"/> is a later addition (issue #23 review) that a deterministic
/// concurrency test pauses at instead, to interleave a second transaction mid-flight rather than to
/// simulate a crash.
/// </summary>
public enum CloudBoundaryFaultPoint
{
    BeforeLocks,

    /// <summary>
    /// Fires once every row lock this transaction needs has been acquired, but before any further
    /// (necessarily plain, non-locking) read -- the earliest point at which a concurrency test can
    /// pause and let a second, independent transaction commit without racing this one's own locks.
    /// </summary>
    AfterLocks,

    AfterValidation,
    AfterPossessionChange,
    AfterCustodyChange,
    AfterLedgerAppend,
    AfterOutboxAppend,
    BeforeCommit,
    AfterCommit,
}
