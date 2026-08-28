namespace ACE.Cloud.Domain;

/// <summary>
/// Pure validated transitions over a <see cref="CloudWithdrawalLocationConfiguration"/> (WDR-006,
/// ADM-003). Every method here is a pure function over its inputs -- it never touches a database --
/// so the exact same admin-facing validation rules run identically whether exercised directly in a
/// unit test or from behind ACE.Cloud.Persistence's locked optimistic-concurrency boundary.
/// </summary>
public static class CloudWithdrawalLocationConfigurationPolicy
{
    /// <summary>
    /// Toggles the audited shard-wide `withdraw anywhere` bypass (WDR-006: "defaults off"). A
    /// same-value toggle is a deliberate no-op and does not bump the version.
    /// </summary>
    public static CloudWithdrawalLocationConfigurationChangeResult SetWithdrawAnywhereEnabled(
        CloudWithdrawalLocationConfiguration current, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.WithdrawAnywhereEnabled == enabled)
        {
            return CloudWithdrawalLocationConfigurationChangeResult.Success(current);
        }

        return CloudWithdrawalLocationConfigurationChangeResult.Success(current with
        {
            WithdrawAnywhereEnabled = enabled,
            Version = current.Version.Next(),
        });
    }

    /// <summary>
    /// Adds one named Withdrawal Landblock. Rejects a landblock that duplicates an existing named
    /// entry -- Marketplace/housing duplicates are not rejected here because those are always-allowed
    /// defaults resolved separately, not members of this list.
    /// </summary>
    public static CloudWithdrawalLocationConfigurationChangeResult AddNamedLandblock(
        CloudWithdrawalLocationConfiguration current, Guid newLandblockId, ushort landblock, string name)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (newLandblockId == Guid.Empty)
        {
            throw new ArgumentException("A new named Withdrawal Landblock requires a real ID.", nameof(newLandblockId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return CloudWithdrawalLocationConfigurationChangeResult.Failure("A named Withdrawal Landblock requires a non-empty name.");
        }

        if (current.NamedLandblocks.Any(existing => existing.Landblock == landblock))
        {
            return CloudWithdrawalLocationConfigurationChangeResult.Failure(
                $"Landblock 0x{landblock:X4} is already a named Withdrawal Landblock.");
        }

        var namedLandblocks = current.NamedLandblocks
            .Append(new CloudWithdrawalNamedLandblock(newLandblockId, landblock, name.Trim()))
            .ToList();

        return CloudWithdrawalLocationConfigurationChangeResult.Success(current with
        {
            NamedLandblocks = namedLandblocks,
            Version = current.Version.Next(),
        });
    }

    public static CloudWithdrawalLocationConfigurationChangeResult RemoveNamedLandblock(
        CloudWithdrawalLocationConfiguration current, Guid landblockId)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.NamedLandblocks.All(existing => existing.Id != landblockId))
        {
            return CloudWithdrawalLocationConfigurationChangeResult.Failure($"No named Withdrawal Landblock with ID {landblockId} exists.");
        }

        var namedLandblocks = current.NamedLandblocks.Where(existing => existing.Id != landblockId).ToList();

        return CloudWithdrawalLocationConfigurationChangeResult.Success(current with
        {
            NamedLandblocks = namedLandblocks,
            Version = current.Version.Next(),
        });
    }
}
