namespace ACE.Cloud.Contracts;

/// <summary>
/// The exhaustive set of outcomes a Cloud boundary command may produce (transaction rules 3, 4,
/// and 8). Every mutation handler must resolve to exactly one of these, never an ambiguous or
/// inferred success.
/// </summary>
public enum CloudCommandResultKind
{
    /// <summary>The command committed a new mutation.</summary>
    Success,

    /// <summary>The command's expected aggregate version did not match the current authoritative version.</summary>
    Conflict,

    /// <summary>The command failed a validation rule before any mutation was attempted.</summary>
    ValidationFailed,

    /// <summary>A required dependency (for example the ACE world process) was unavailable.</summary>
    Unavailable,

    /// <summary>The command's idempotency key matched a previously committed attempt; its stored result was replayed.</summary>
    IdempotentReplay,
}
