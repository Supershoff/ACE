namespace ACE.Cloud.Persistence;

/// <summary>
/// The single counter row that assigns each <see cref="CloudLiveStreamEvent"/> its durable, strictly
/// increasing <see cref="CloudLiveStreamEvent.SequenceNumber"/>, the exact same row-locking approach
/// <see cref="CloudCustodyOutboxSequence"/> uses for the Custody Outbox. One row exists per
/// deployment (ARCH-001).
/// </summary>
public sealed class CloudLiveStreamSequence
{
    private CloudLiveStreamSequence()
    {
    }

    public int Id { get; private set; } = 1;

    public long NextValue { get; private set; }
}
