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

    /// <summary>
    /// Only meaningful for <see cref="CloudCustodianDepositRowDecisionKind.ConvertPyreal"/>: the
    /// exact raw Pyreal amount to combine with the account's existing Pyreal Remainder (DEP-006).
    /// </summary>
    public long RawPyrealAmount { get; }

    /// <summary>Only set for <see cref="CloudCustodianDepositRowDecisionKind.Reject"/>.</summary>
    public string? PlayerMessage { get; }

    /// <summary>Only set for <see cref="CloudCustodianDepositRowDecisionKind.Reject"/>.</summary>
    public CloudEligibilityRejectionCode? RejectionCode { get; }

    public IReadOnlyList<CloudRuntimeEnchantmentSnapshot> PreservationRequirements { get; }

    private CloudCustodianDepositRowDecision(
        CloudCustodianDepositRowDecisionKind kind,
        CloudItemId itemId,
        int quantity,
        long rawPyrealAmount,
        string? playerMessage,
        CloudEligibilityRejectionCode? rejectionCode,
        IReadOnlyList<CloudRuntimeEnchantmentSnapshot> preservationRequirements)
    {
        Kind = kind;
        ItemId = itemId;
        Quantity = quantity;
        RawPyrealAmount = rawPyrealAmount;
        PlayerMessage = playerMessage;
        RejectionCode = rejectionCode;
        PreservationRequirements = preservationRequirements;
    }

    public static CloudCustodianDepositRowDecision DepositWhole(
        CloudItemId itemId, IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements = null) =>
        new(CloudCustodianDepositRowDecisionKind.DepositWhole, itemId, 1, 0, null, null, preservationRequirements ?? []);

    public static CloudCustodianDepositRowDecision DepositStack(
        CloudItemId itemId, int quantity, IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements = null)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A stack deposit decision requires a positive quantity.");
        }

        return new(CloudCustodianDepositRowDecisionKind.DepositStack, itemId, quantity, 0, null, null, preservationRequirements ?? []);
    }

    /// <summary>
    /// Converts a raw Pyreal coin-stack row instead of depositing it as itself (DEP-006).
    /// </summary>
    public static CloudCustodianDepositRowDecision ConvertPyreal(CloudItemId itemId, long rawPyrealAmount)
    {
        if (rawPyrealAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rawPyrealAmount), "A Pyreal conversion decision requires a positive raw amount.");
        }

        return new(CloudCustodianDepositRowDecisionKind.ConvertPyreal, itemId, 0, rawPyrealAmount, null, null, []);
    }

    public static CloudCustodianDepositRowDecision Reject(CloudItemId itemId, string playerMessage, CloudEligibilityRejectionCode? rejectionCode = null)
    {
        if (string.IsNullOrWhiteSpace(playerMessage))
        {
            throw new ArgumentException("A rejected deposit row requires an actionable in-game message.", nameof(playerMessage));
        }

        return new(CloudCustodianDepositRowDecisionKind.Reject, itemId, 0, 0, playerMessage, rejectionCode, []);
    }
}
