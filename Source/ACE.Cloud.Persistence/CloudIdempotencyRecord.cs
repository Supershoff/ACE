namespace ACE.Cloud.Persistence;

/// <summary>
/// Proves a world-boundary handoff attempt already ran to completion (ARCH-006, transaction rules
/// 4 and 8): repeating a request with the same <see cref="IdempotencyKey"/> must replay this
/// committed result rather than reapplying the ownership change. Written in the same database
/// transaction as the custody, ledger, and outbox rows it describes.
/// </summary>
public sealed class CloudIdempotencyRecord
{
    private CloudIdempotencyRecord()
    {
    }

    public CloudIdempotencyRecord(
        Guid idempotencyKey,
        string shardId,
        CloudBoundaryOperationType operationType,
        uint biotaId,
        Guid ownerId,
        Guid? custodyRecordId,
        uint? targetContainerId,
        Guid correlationId,
        int? quantity = null)
    {
        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("An idempotency record requires a non-empty key.", nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An idempotency record requires a Cloud Shard ID.", nameof(shardId));
        }

        if (biotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(biotaId), "An idempotency record requires a real native biota GUID.");
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("An idempotency record requires an owner.", nameof(ownerId));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("An idempotency record requires an Activity Ledger correlation ID.", nameof(correlationId));
        }

        if (operationType is CloudBoundaryOperationType.Withdrawal or CloudBoundaryOperationType.StackWithdrawal
            && targetContainerId is null or 0)
        {
            throw new ArgumentException(
                "A withdrawal idempotency record requires the recipient container it delivered into.", nameof(targetContainerId));
        }

        if (operationType == CloudBoundaryOperationType.StackWithdrawal && quantity is null or <= 0)
        {
            throw new ArgumentException(
                "A stack withdrawal idempotency record requires the positive quantity it delivered.", nameof(quantity));
        }

        IdempotencyKey = idempotencyKey;
        ShardId = shardId;
        OperationType = operationType;
        BiotaId = biotaId;
        OwnerId = ownerId;
        CustodyRecordId = custodyRecordId;
        TargetContainerId = targetContainerId;
        CorrelationId = correlationId;
        Quantity = quantity;
    }

    public Guid IdempotencyKey { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudBoundaryOperationType OperationType { get; private set; }

    public uint BiotaId { get; private set; }

    /// <summary>
    /// The Cloud owner at the time of this operation: the depositing owner for a Deposit, the
    /// withdrawing (former) owner for a Withdrawal.
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// The <see cref="CloudCustodyRecord"/> created (Deposit) or consumed (Withdrawal). A
    /// withdrawal deletes that row in the same transaction, so this is not a foreign key: the
    /// referenced row may no longer exist, and replay must not depend on it still existing.
    /// </summary>
    public Guid? CustodyRecordId { get; private set; }

    /// <summary>
    /// The recipient container a withdrawal delivered into; null for a Deposit.
    /// </summary>
    public uint? TargetContainerId { get; private set; }

    public Guid CorrelationId { get; private set; }

    /// <summary>
    /// The quantity a StackDeposit or StackWithdrawal delivered; null for a non-stack operation.
    /// </summary>
    public int? Quantity { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
