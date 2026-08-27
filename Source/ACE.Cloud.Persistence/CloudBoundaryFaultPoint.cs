namespace ACE.Cloud.Persistence;

/// <summary>
/// Every boundary named in issue #4's Red section at which a crash-safety test injects a simulated
/// crash. <see cref="CloudCustodyBoundary"/> invokes an internal, test-only fault injector at each
/// of these points; production callers never supply one (see the internal overloads' doc comments).
/// </summary>
public enum CloudBoundaryFaultPoint
{
    BeforeLocks,
    AfterValidation,
    AfterPossessionChange,
    AfterCustodyChange,
    AfterLedgerAppend,
    AfterOutboxAppend,
    BeforeCommit,
    AfterCommit,
}
