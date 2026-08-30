namespace ACE.Cloud.Persistence;

/// <summary>
/// One outbox projection consumer's durable resume position (ARCH-007's "the web consumes events
/// idempotently and can rebuild all read/search projections"). A consumer persists this in the same
/// transaction as the projection row(s) it just updated, so a crash between events leaves the
/// checkpoint pointing at exactly the last event that was actually applied -- restart simply resumes
/// from here, matching issue #22's Red "consumer restart"/"checkpoint loss" cases: losing this row
/// (or never having written it) is indistinguishable from "nothing consumed yet" and safely replays
/// from the beginning.
/// </summary>
public sealed class CloudProjectionCheckpoint
{
    private CloudProjectionCheckpoint()
    {
    }

    public CloudProjectionCheckpoint(string consumerName, string shardId)
    {
        if (string.IsNullOrWhiteSpace(consumerName))
        {
            throw new ArgumentException("A projection checkpoint requires a consumer name.", nameof(consumerName));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A projection checkpoint requires a Cloud Shard ID.", nameof(shardId));
        }

        ConsumerName = consumerName;
        ShardId = shardId;
        LastAppliedSequenceNumber = 0;
    }

    /// <summary>Stable identity of the consumer this checkpoint belongs to, for example "CustodyProjection".</summary>
    public string ConsumerName { get; private set; } = null!;

    public string ShardId { get; private set; } = null!;

    /// <summary>
    /// The highest outbox <c>SequenceNumber</c> this consumer has durably applied; 0 means nothing
    /// has been consumed yet (mirrors <see cref="CloudCustodyOutboxReader.ReadAfterAsync"/>'s "pass 0
    /// to read from the very beginning" convention).
    /// </summary>
    public long LastAppliedSequenceNumber { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Advances this checkpoint after a single event has been durably applied (or diagnosed and skipped).</summary>
    internal void Advance(long sequenceNumber)
    {
        if (sequenceNumber <= LastAppliedSequenceNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceNumber), "A checkpoint can only advance to a strictly higher sequence number.");
        }

        LastAppliedSequenceNumber = sequenceNumber;
    }

    /// <summary>Resets this checkpoint back to empty for a full rebuild (issue #22's Green "full rebuild commands").</summary>
    internal void ResetForRebuild()
    {
        LastAppliedSequenceNumber = 0;
    }
}
