namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter"/>: either
/// approved, carrying the exact monarch identifying which Allegiance Vault the Acting Character just
/// authorized, or refused with one exact <see cref="CloudAllegianceVaultActionRejectionCode"/>.
/// </summary>
public sealed record CloudAllegianceVaultActionResult
{
    public bool IsSuccess { get; }

    /// <summary>The authorized Allegiance Vault's monarch character ID. Only meaningful when <see cref="IsSuccess"/> is true.</summary>
    public uint VaultMonarchId { get; }

    public CloudAllegianceVaultActionRejectionCode RejectionCode { get; }

    public string? Reason { get; }

    private CloudAllegianceVaultActionResult(bool isSuccess, uint vaultMonarchId, CloudAllegianceVaultActionRejectionCode rejectionCode, string? reason)
    {
        IsSuccess = isSuccess;
        VaultMonarchId = vaultMonarchId;
        RejectionCode = rejectionCode;
        Reason = reason;
    }

    public static CloudAllegianceVaultActionResult Success(uint vaultMonarchId)
    {
        if (vaultMonarchId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vaultMonarchId), "An authorized Allegiance Vault action requires a real monarch character GUID.");
        }

        return new CloudAllegianceVaultActionResult(true, vaultMonarchId, CloudAllegianceVaultActionRejectionCode.None, reason: null);
    }

    public static CloudAllegianceVaultActionResult Failure(CloudAllegianceVaultActionRejectionCode rejectionCode, string reason)
    {
        if (rejectionCode == CloudAllegianceVaultActionRejectionCode.None)
        {
            throw new ArgumentException("A refused Allegiance Vault action requires a real rejection code.", nameof(rejectionCode));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A refused Allegiance Vault action requires a reason.", nameof(reason));
        }

        return new CloudAllegianceVaultActionResult(false, 0, rejectionCode, reason);
    }
}
