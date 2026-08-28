namespace ACE.Cloud.Domain;

public enum CloudAccountLoginEligibilityKind
{
    Eligible,
    Banned,
}

public sealed record CloudAccountLoginEligibilityResult(CloudAccountLoginEligibilityKind Kind, string? Reason)
{
    public bool IsEligible => Kind == CloudAccountLoginEligibilityKind.Eligible;

    public static CloudAccountLoginEligibilityResult Eligible() => new(CloudAccountLoginEligibilityKind.Eligible, Reason: null);

    public static CloudAccountLoginEligibilityResult Banned(string reason) => new(CloudAccountLoginEligibilityKind.Banned, reason);
}

/// <summary>
/// Mirrors ACE's own native login ban gate
/// (<c>Server/Network/Handlers/AuthenticationHandler.cs</c>: <c>if (account.BanExpireTime.HasValue)
/// ... if (now &lt; account.BanExpireTime.Value)</c>) so an ACE-backed Login is refused under exactly
/// the same rule as the game client, not a web-invented approximation of it. A <c>BannedTime</c> with
/// no <c>BanExpireTime</c> is not currently blocking, matching that same native behavior.
/// </summary>
public static class CloudAccountLoginEligibilityPolicy
{
    public static CloudAccountLoginEligibilityResult Evaluate(CloudAceAccountSnapshot account, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.BanExpireTime is { } banExpireTime && nowUtc < banExpireTime)
        {
            var reason = string.IsNullOrWhiteSpace(account.BanReason)
                ? "This account is banned."
                : $"This account is banned: {account.BanReason}";

            return CloudAccountLoginEligibilityResult.Banned(reason);
        }

        return CloudAccountLoginEligibilityResult.Eligible();
    }
}
