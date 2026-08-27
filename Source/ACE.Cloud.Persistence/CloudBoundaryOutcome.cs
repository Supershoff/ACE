namespace ACE.Cloud.Persistence;

/// <summary>
/// The result of one world-boundary handoff attempt. Callers must branch on <see cref="Kind"/>
/// instead of relying on exceptions for expected outcomes (transaction rule 8: never infer success
/// or failure from anything other than the authoritative committed result).
/// </summary>
public sealed record CloudBoundaryOutcome<T>
{
    private CloudBoundaryOutcome(CloudBoundaryOutcomeKind kind, T? value, string? reason)
    {
        Kind = kind;
        Value = value;
        Reason = reason;
    }

    public CloudBoundaryOutcomeKind Kind { get; }

    /// <summary>
    /// Populated only when <see cref="Kind"/> is <see cref="CloudBoundaryOutcomeKind.Committed"/>.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Populated only when <see cref="Kind"/> is Conflict or Unavailable.
    /// </summary>
    public string? Reason { get; }

    public static CloudBoundaryOutcome<T> Committed(T value) =>
        new(CloudBoundaryOutcomeKind.Committed, value ?? throw new ArgumentNullException(nameof(value)), reason: null);

    public static CloudBoundaryOutcome<T> Conflict(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A conflict outcome requires a reason.", nameof(reason));
        }

        return new CloudBoundaryOutcome<T>(CloudBoundaryOutcomeKind.Conflict, value: default, reason);
    }

    public static CloudBoundaryOutcome<T> Unavailable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An unavailable outcome requires a reason.", nameof(reason));
        }

        return new CloudBoundaryOutcome<T>(CloudBoundaryOutcomeKind.Unavailable, value: default, reason);
    }
}
