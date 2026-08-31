namespace ACE.Cloud.Persistence;

/// <summary>
/// A poison outbox event a projection consumer could not apply, recorded so it stops blocking every
/// later event instead of retrying forever (issue #22's Green "durable checkpoints/dead-letter
/// diagnostics"). The projection this consumer maintains is a rebuildable search/read model, not
/// authoritative state (ARCH-012: "search uses a rebuildable indexed read model"), so skipping past
/// one bad event and diagnosing it here is safe -- MariaDB's authoritative Custody/Identity Outbox
/// rows are completely unaffected and a later full rebuild reproduces the identical skip
/// deterministically, keeping the "clean rebuild matches incremental consumption" acceptance
/// criterion true even in the presence of a poison event.
/// </summary>
public sealed class CloudProjectionDeadLetter
{
    private CloudProjectionDeadLetter()
    {
    }

    public CloudProjectionDeadLetter(string consumerName, string shardId, Guid sourceEventId, long sourceSequenceNumber, string reason)
    {
        if (string.IsNullOrWhiteSpace(consumerName))
        {
            throw new ArgumentException("A dead-letter entry requires a consumer name.", nameof(consumerName));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A dead-letter entry requires a Cloud Shard ID.", nameof(shardId));
        }

        if (sourceEventId == Guid.Empty)
        {
            throw new ArgumentException("A dead-letter entry requires the source event's ID.", nameof(sourceEventId));
        }

        if (sourceSequenceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSequenceNumber), "A dead-letter entry requires a positive source sequence number.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A dead-letter entry requires a reason.", nameof(reason));
        }

        Id = Guid.NewGuid();
        ConsumerName = consumerName;
        ShardId = shardId;
        SourceEventId = sourceEventId;
        SourceSequenceNumber = sourceSequenceNumber;
        Reason = reason.Length > 512 ? reason[..512] : reason;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public string ConsumerName { get; private set; } = null!;

    public string ShardId { get; private set; } = null!;

    public Guid SourceEventId { get; private set; }

    public long SourceSequenceNumber { get; private set; }

    /// <summary>
    /// A bounded, operator-safe description of the failure. Must never carry a raw exception message
    /// verbatim (see <c>CloudProjectionFailureDescriber</c>): a raw message can embed absolute
    /// operator paths or other detail unsafe for this diagnostic surface, mirroring
    /// <c>CloudAssetImportStagingWorker.DescribeExtractionFailure</c>'s existing redaction discipline.
    /// </summary>
    public string Reason { get; private set; } = null!;

    public DateTime CreatedAtUtc { get; private set; }
}
