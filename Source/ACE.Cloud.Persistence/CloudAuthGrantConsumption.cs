namespace ACE.Cloud.Persistence;

/// <summary>
/// Records that a signed ACE Auth Bridge grant's nonce was already exchanged for a session
/// (AUTH-002's "one-use grant"). The Auth Bridge cannot track this itself -- it has no Cloud schema
/// access at all (ARCH-004) -- so the Cloud backend, which does own this schema, records consumption
/// the moment it independently verifies a grant's signature; a unique constraint on
/// <see cref="Nonce"/> makes a replayed grant fail with a duplicate-key error rather than silently
/// issuing a second session.
/// </summary>
public sealed class CloudAuthGrantConsumption
{
    private CloudAuthGrantConsumption()
    {
    }

    public CloudAuthGrantConsumption(Guid nonce, uint accountId, DateTime consumedAtUtc)
    {
        if (nonce == Guid.Empty)
        {
            throw new ArgumentException("A grant consumption record requires a non-empty nonce.", nameof(nonce));
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A grant consumption record requires a real ACE account ID.");
        }

        Nonce = nonce;
        AccountId = accountId;
        ConsumedAtUtc = consumedAtUtc;
    }

    public Guid Nonce { get; private set; }

    public uint AccountId { get; private set; }

    public DateTime ConsumedAtUtc { get; private set; }
}
