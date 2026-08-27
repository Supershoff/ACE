namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudItemEligibilityPolicy.Evaluate"/>: either eligible, carrying any
/// Frozen Enchantment preservation requirements (DEP-005), or ineligible, carrying a stable
/// <see cref="CloudEligibilityRejectionCode"/> plus both an actionable in-game message and a safe web
/// message (DEP-002: "report the exact item-specific reason").
/// </summary>
public sealed record CloudEligibilityResult
{
    public bool IsEligible { get; }

    public CloudEligibilityRejectionCode? RejectionCode { get; }

    public string? PlayerMessage { get; }

    public string? WebMessage { get; }

    public IReadOnlyList<CloudRuntimeEnchantmentSnapshot> PreservationRequirements { get; }

    private CloudEligibilityResult(
        bool isEligible,
        CloudEligibilityRejectionCode? rejectionCode,
        string? playerMessage,
        string? webMessage,
        IReadOnlyList<CloudRuntimeEnchantmentSnapshot> preservationRequirements)
    {
        IsEligible = isEligible;
        RejectionCode = rejectionCode;
        PlayerMessage = playerMessage;
        WebMessage = webMessage;
        PreservationRequirements = preservationRequirements;
    }

    public static CloudEligibilityResult Eligible(IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements = null) =>
        new(true, null, null, null, preservationRequirements ?? []);

    public static CloudEligibilityResult Ineligible(CloudEligibilityRejectionCode rejectionCode, string playerMessage, string webMessage)
    {
        if (string.IsNullOrWhiteSpace(playerMessage))
        {
            throw new ArgumentException("A rejected eligibility result requires an actionable in-game message.", nameof(playerMessage));
        }

        if (string.IsNullOrWhiteSpace(webMessage))
        {
            throw new ArgumentException("A rejected eligibility result requires a safe web message.", nameof(webMessage));
        }

        return new CloudEligibilityResult(false, rejectionCode, playerMessage, webMessage, []);
    }
}
