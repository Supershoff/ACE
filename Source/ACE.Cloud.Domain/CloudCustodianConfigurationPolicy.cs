namespace ACE.Cloud.Domain;

/// <summary>
/// Pure validated transitions over a <see cref="CloudCustodianConfiguration"/> (DEP-007, ADM-003).
/// Every method here is a pure function over its inputs -- it never touches a database -- so the
/// exact same admin-facing validation rules (parse, duplicate-reject, version-bump-only-on-change)
/// run identically whether they are exercised directly in a unit test or from behind
/// ACE.Cloud.Persistence's locked optimistic-concurrency boundary.
/// </summary>
public static class CloudCustodianConfigurationPolicy
{
    public static CloudCustodianConfigurationChangeResult SetMarketplaceEnabled(CloudCustodianConfiguration current, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(current);

        // A same-value toggle is a deliberate no-op: it must not bump the version and thereby
        // invalidate every currently open, still-valid Custodian sell window (DEP-008's stale-window
        // protection exists to catch real configuration changes, not idempotent re-sends of the
        // current state).
        if (current.MarketplaceEnabled == enabled)
        {
            return CloudCustodianConfigurationChangeResult.Success(current);
        }

        return CloudCustodianConfigurationChangeResult.Success(current with
        {
            MarketplaceEnabled = enabled,
            Version = current.Version.Next(),
        });
    }

    public static CloudCustodianConfigurationChangeResult SetMansionsEnabled(CloudCustodianConfiguration current, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.MansionsEnabled == enabled)
        {
            return CloudCustodianConfigurationChangeResult.Success(current);
        }

        return CloudCustodianConfigurationChangeResult.Success(current with
        {
            MansionsEnabled = enabled,
            Version = current.Version.Next(),
        });
    }

    /// <summary>
    /// Adds one custom Custodian Location (DEP-007: "custom full ACE position strings"). Rejects an
    /// unparsable string and a position that duplicates an existing custom position (DEP-007's Red
    /// tests: "duplicate/invalid positions"). Duplicates against the Marketplace/Mansion sets are
    /// caught separately, at resolution time (<see cref="CloudCustodianLocationResolver"/>), because
    /// this policy has no visibility into the operator's live world content.
    /// </summary>
    public static CloudCustodianConfigurationChangeResult AddCustomPosition(
        CloudCustodianConfiguration current, Guid newPositionId, string rawPosition)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (newPositionId == Guid.Empty)
        {
            throw new ArgumentException("A new custom position requires a real ID.", nameof(newPositionId));
        }

        var parsed = CloudCustodianPosition.TryParse(rawPosition);
        if (parsed is null)
        {
            return CloudCustodianConfigurationChangeResult.Failure(
                $"\"{rawPosition}\" is not a valid ACE position string. Expected format: 0xLLLLLLLL [x y z] qw qx qy qz.");
        }

        if (current.CustomPositions.Any(existing => existing.Position.Equals(parsed)))
        {
            return CloudCustodianConfigurationChangeResult.Failure(
                $"\"{rawPosition}\" duplicates an existing custom Custodian Location.");
        }

        var customPositions = current.CustomPositions.Append(new CloudCustodianCustomPosition(newPositionId, parsed)).ToList();

        return CloudCustodianConfigurationChangeResult.Success(current with
        {
            CustomPositions = customPositions,
            Version = current.Version.Next(),
        });
    }

    public static CloudCustodianConfigurationChangeResult RemoveCustomPosition(CloudCustodianConfiguration current, Guid positionId)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.CustomPositions.All(existing => existing.Id != positionId))
        {
            return CloudCustodianConfigurationChangeResult.Failure($"No custom Custodian Location with ID {positionId} exists.");
        }

        var customPositions = current.CustomPositions.Where(existing => existing.Id != positionId).ToList();

        return CloudCustodianConfigurationChangeResult.Success(current with
        {
            CustomPositions = customPositions,
            Version = current.Version.Next(),
        });
    }
}
