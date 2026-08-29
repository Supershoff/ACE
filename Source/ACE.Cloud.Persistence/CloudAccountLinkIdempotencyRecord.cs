using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Proves one link/unlink attempt already ran to completion (ARCH-006, transaction rules 4 and 8),
/// including a rejected outcome: replaying the same idempotency key must report the exact same
/// rejection reason as the first attempt rather than re-running (and potentially re-deciding,
/// against since-changed state) the eligibility check. Kept separate from the biota-shaped
/// <see cref="CloudIdempotencyRecord"/> for the same reason <see cref="CloudIdentityOutboxEvent"/> is
/// separate from <see cref="CloudCustodyOutboxEvent"/>: an account link touches no native biota or
/// custody record identity of its own.
/// </summary>
public sealed class CloudAccountLinkIdempotencyRecord
{
    private CloudAccountLinkIdempotencyRecord()
    {
    }

    public CloudAccountLinkIdempotencyRecord(
        Guid idempotencyKey,
        string shardId,
        CloudAccountLinkOperationType operationType,
        uint mainAccountId,
        uint sourceAccountId,
        bool isApproved,
        CloudAccountLinkRejectionCode rejectionCode,
        Guid? accountLinkId,
        Guid? ownershipGroupId,
        Guid correlationId)
    {
        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("An account link idempotency record requires a non-empty key.", nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An account link idempotency record requires a Cloud Shard ID.", nameof(shardId));
        }

        if (mainAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mainAccountId), "An account link idempotency record requires a real Main Account ID.");
        }

        if (sourceAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAccountId), "An account link idempotency record requires a real source account ID.");
        }

        if (isApproved && (accountLinkId is null || ownershipGroupId is null))
        {
            throw new ArgumentException(
                "An approved account link idempotency record requires the account link and ownership group it created/affected.",
                nameof(accountLinkId));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("An account link idempotency record requires a correlation ID.", nameof(correlationId));
        }

        IdempotencyKey = idempotencyKey;
        ShardId = shardId;
        OperationType = operationType;
        MainAccountId = mainAccountId;
        SourceAccountId = sourceAccountId;
        IsApproved = isApproved;
        RejectionCode = rejectionCode;
        AccountLinkId = accountLinkId;
        OwnershipGroupId = ownershipGroupId;
        CorrelationId = correlationId;
    }

    public Guid IdempotencyKey { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudAccountLinkOperationType OperationType { get; private set; }

    public uint MainAccountId { get; private set; }

    public uint SourceAccountId { get; private set; }

    public bool IsApproved { get; private set; }

    public CloudAccountLinkRejectionCode RejectionCode { get; private set; }

    /// <summary>The <see cref="CloudAccountLink"/> created (Link) or ended (Unlink); null for a rejected attempt.</summary>
    public Guid? AccountLinkId { get; private set; }

    /// <summary>The affected <see cref="CloudOwnershipGroup"/>; null for a rejected attempt.</summary>
    public Guid? OwnershipGroupId { get; private set; }

    public Guid CorrelationId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
