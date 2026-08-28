namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudMonarchDeletionGuard.Evaluate"/>.
/// </summary>
public sealed record CloudMonarchDeletionDecision
{
    public bool IsAllowed { get; }

    public string? Reason { get; }

    private CloudMonarchDeletionDecision(bool isAllowed, string? reason)
    {
        IsAllowed = isAllowed;
        Reason = reason;
    }

    public static CloudMonarchDeletionDecision Allow() => new(true, null);

    public static CloudMonarchDeletionDecision Block(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A blocked monarch deletion requires a reason.", nameof(reason));
        }

        return new CloudMonarchDeletionDecision(false, reason);
    }
}
