namespace ACE.Cloud.Persistence;

/// <summary>
/// Whether the Cloud schema database is currently reachable (ARCH-009). Diagnostic-only: unlike
/// <see cref="CloudBoundaryOutcome{T}.Unavailable"/>, which a mutation returns after already
/// attempting and failing, this is a read-only probe health/recovery tooling can call on its own.
/// </summary>
public sealed record CloudGatewayAvailabilityResult
{
    private CloudGatewayAvailabilityResult(bool isAvailable, string? reason)
    {
        IsAvailable = isAvailable;
        Reason = reason;
    }

    public bool IsAvailable { get; }

    public string? Reason { get; }

    public static CloudGatewayAvailabilityResult Available() => new(true, null);

    public static CloudGatewayAvailabilityResult Unavailable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An unavailable result requires a reason.", nameof(reason));
        }

        return new CloudGatewayAvailabilityResult(false, reason);
    }
}
