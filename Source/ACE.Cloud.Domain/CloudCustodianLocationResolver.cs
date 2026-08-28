namespace ACE.Cloud.Domain;

/// <summary>
/// Combines a <see cref="CloudCustodianConfiguration"/> with the caller-supplied Marketplace and
/// Mansion positions into the exact set of places a Cloud Custodian should currently be spawned
/// (DEP-007, DEP-008). Pure and world-free: ACE.Server is responsible for resolving the current
/// Marketplace position and enumerating live Mansion plots from ace_world (there is no fixed,
/// shippable list of "every mansion" -- housing plots are per-shard world content) and passing the
/// results in here every time it needs to reapply configuration.
/// </summary>
public static class CloudCustodianLocationResolver
{
    public static IReadOnlyList<CloudCustodianLocation> Resolve(
        CloudCustodianConfiguration configuration,
        CloudCustodianPosition? marketplacePosition,
        IReadOnlyList<CloudCustodianMansionLocation> mansionLocations)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(mansionLocations);

        var resolved = new List<CloudCustodianLocation>();
        var seenPositions = new List<CloudCustodianPosition>();

        void AddIfNotDuplicate(CloudCustodianLocationKey key, CloudCustodianPosition position)
        {
            // Two different location sources (for example a custom position an administrator
            // typed that happens to match the Marketplace drop point) never spawn two Custodians
            // on top of each other (DEP-007: "each enabled Custodian occupies one location").
            if (seenPositions.Any(position.Equals))
            {
                return;
            }

            seenPositions.Add(position);
            resolved.Add(new CloudCustodianLocation(key, position));
        }

        if (configuration.MarketplaceEnabled && marketplacePosition is not null)
        {
            AddIfNotDuplicate(CloudCustodianLocationKey.Marketplace, marketplacePosition);
        }

        if (configuration.MansionsEnabled)
        {
            foreach (var mansion in mansionLocations)
            {
                AddIfNotDuplicate(CloudCustodianLocationKey.ForMansion(mansion.MansionGuid), mansion.Position);
            }
        }

        foreach (var custom in configuration.CustomPositions)
        {
            AddIfNotDuplicate(CloudCustodianLocationKey.ForCustom(custom.Id), custom.Position);
        }

        return resolved;
    }
}
