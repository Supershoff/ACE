namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudCustodianDepositRowPolicy.Decide"/> for one submitted row
/// (DEP-002: "report the exact item-specific reason").
/// </summary>
public sealed record CloudCustodianDepositRowDecision
{
    public CloudCustodianDepositRowDecisionKind Kind { get; }

    public CloudItemId ItemId { get; }

    /// <summary>Only meaningful for <see cref="CloudCustodianDepositRowDecisionKind.DepositStack"/>.</summary>
    public int Quantity { get; }

    /// <summary>Only set for <see cref="CloudCustodianDepositRowDecisionKind.Reject"/>.</summary>
    public string? PlayerMessage { get; }

    /// <summary>Only set for <see cref="CloudCustodianDepositRowDecisionKind.Reject"/>.</summary>
    public CloudEligibilityRejectionCode? RejectionCode { get; }

    public IReadOnlyList<CloudRuntimeEnchantmentSnapshot> PreservationRequirements { get; }

    private CloudCustodianDepositRowDecision(
        CloudCustodianDepositRowDecisionKind kind,
        CloudItemId itemId,
        int quantity,
        string? playerMessage,
        CloudEligibilityRejectionCode? rejectionCode,
        IReadOnlyList<CloudRuntimeEnchantmentSnapshot> preservationRequirements)
    {
        Kind = kind;
        ItemId = itemId;
        Quantity = quantity;
        PlayerMessage = playerMessage;
        RejectionCode = rejectionCode;
        PreservationRequirements = preservationRequirements;
    }

    public static CloudCustodianDepositRowDecision DepositWhole(
        CloudItemId itemId, IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements = null) =>
        new(CloudCustodianDepositRowDecisionKind.DepositWhole, itemId, 1, null, null, preservationRequirements ?? []);

    public static CloudCustodianDepositRowDecision DepositStack(
        CloudItemId itemId, int quantity, IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements = null)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A stack deposit decision requires a positive quantity.");
        }

        return new(CloudCustodianDepositRowDecisionKind.DepositStack, itemId, quantity, null, null, preservationRequirements ?? []);
    }

    public static CloudCustodianDepositRowDecision Reject(CloudItemId itemId, string playerMessage, CloudEligibilityRejectionCode? rejectionCode = null)
    {
        if (string.IsNullOrWhiteSpace(playerMessage))
        {
            throw new ArgumentException("A rejected deposit row requires an actionable in-game message.", nameof(playerMessage));
        }

        return new(CloudCustodianDepositRowDecisionKind.Reject, itemId, 0, playerMessage, rejectionCode, []);
    }
}
