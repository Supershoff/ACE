namespace ACE.Cloud.Domain;

/// <summary>
/// Pure rules for changing <see cref="CloudSearchConfiguration"/> (SRCH-001: "Admin can disable regex
/// independently"), matching <see cref="CloudMarketplaceStatePolicy"/>'s established
/// revalidate-then-transition shape for a singleton admin-config aggregate.
/// </summary>
public static class CloudSearchConfigurationPolicy
{
    public static CloudSearchConfigurationChangeResult SetRegexSearchEnabled(
        CloudSearchConfiguration current, bool requested, uint actorAccessLevel)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!CloudAdminAccessRevalidationPolicy.Evaluate(actorAccessLevel).IsAuthorized)
        {
            return CloudSearchConfigurationChangeResult.Failure(
                "Only an administrator (ace_auth.account.accessLevel == 5) may change the Safe Regex Search setting.");
        }

        if (current.RegexSearchEnabled == requested)
        {
            return CloudSearchConfigurationChangeResult.Success(current);
        }

        return CloudSearchConfigurationChangeResult.Success(current with
        {
            RegexSearchEnabled = requested,
            Version = current.Version.Next(),
        });
    }
}
