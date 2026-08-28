namespace ACE.Cloud.Persistence;

/// <summary>
/// One MMD created by a <see cref="CloudPyrealConversionRecord"/> (DEP-006): the ACE-allocated
/// native biota GUID ACE materialized off-world before calling
/// <c>CloudCustodyBoundary.ConvertPyrealDepositAsync</c> (ARCH-002, ARCH-010: only ACE allocates a
/// GUID), and the whole-item <see cref="CloudCustodyRecord"/> this conversion created for it.
/// </summary>
public sealed class CloudPyrealConversionMmd
{
    private CloudPyrealConversionMmd()
    {
    }

    public CloudPyrealConversionMmd(Guid conversionIdempotencyKey, uint mmdBiotaId, Guid custodyRecordId)
    {
        if (conversionIdempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("A conversion MMD row requires its owning conversion's idempotency key.", nameof(conversionIdempotencyKey));
        }

        if (mmdBiotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mmdBiotaId), "A conversion MMD row requires a real native biota GUID.");
        }

        if (custodyRecordId == Guid.Empty)
        {
            throw new ArgumentException("A conversion MMD row requires the Cloud Custody Record it created.", nameof(custodyRecordId));
        }

        Id = Guid.NewGuid();
        ConversionIdempotencyKey = conversionIdempotencyKey;
        MmdBiotaId = mmdBiotaId;
        CustodyRecordId = custodyRecordId;
    }

    public Guid Id { get; private set; }

    public Guid ConversionIdempotencyKey { get; private set; }

    public uint MmdBiotaId { get; private set; }

    public Guid CustodyRecordId { get; private set; }
}
