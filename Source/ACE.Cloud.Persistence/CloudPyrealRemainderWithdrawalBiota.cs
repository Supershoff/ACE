namespace ACE.Cloud.Persistence;

/// <summary>
/// One native Pyreal coin-stack biota a <see cref="CloudPyrealRemainderWithdrawalRecord"/>
/// delivered to the recipient container (DEP-006). A single withdrawal may deliver more than one
/// coin-stack biota when the withdrawn amount exceeds one stack's MaxStackSize, exactly like ACE's
/// own ordinary vendor-payout coin creation already chunks a large payout across several stacks
/// (<c>Player_Commerce.CreatePayoutCoinStacks</c>).
/// </summary>
public sealed class CloudPyrealRemainderWithdrawalBiota
{
    private CloudPyrealRemainderWithdrawalBiota()
    {
    }

    public CloudPyrealRemainderWithdrawalBiota(Guid withdrawalIdempotencyKey, uint biotaId)
    {
        if (withdrawalIdempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("A withdrawal biota row requires its owning withdrawal's idempotency key.", nameof(withdrawalIdempotencyKey));
        }

        if (biotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(biotaId), "A withdrawal biota row requires a real native biota GUID.");
        }

        Id = Guid.NewGuid();
        WithdrawalIdempotencyKey = withdrawalIdempotencyKey;
        BiotaId = biotaId;
    }

    public Guid Id { get; private set; }

    public Guid WithdrawalIdempotencyKey { get; private set; }

    public uint BiotaId { get; private set; }
}
