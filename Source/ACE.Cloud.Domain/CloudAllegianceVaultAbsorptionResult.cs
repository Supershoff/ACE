namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudAllegianceVaultAbsorptionPolicy.Absorb"/>.
/// </summary>
public sealed record CloudAllegianceVaultAbsorptionResult
{
    public bool IsSuccess { get; }

    public CloudCustodyTransitionErrorKind? ErrorKind { get; }

    public string? Reason { get; }

    private CloudAllegianceVaultAbsorptionResult(bool isSuccess, CloudCustodyTransitionErrorKind? errorKind, string? reason)
    {
        IsSuccess = isSuccess;
        ErrorKind = errorKind;
        Reason = reason;
    }

    public static CloudAllegianceVaultAbsorptionResult Success() => new(true, null, null);

    public static CloudAllegianceVaultAbsorptionResult Failure(CloudCustodyTransitionErrorKind errorKind, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A failed Vault Absorption requires a reason.", nameof(reason));
        }

        return new CloudAllegianceVaultAbsorptionResult(false, errorKind, reason);
    }
}
