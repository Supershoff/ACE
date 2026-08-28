namespace ACE.Cloud.Persistence;

/// <summary>
/// The single counter row that assigns each <see cref="CloudIdentityOutboxEvent"/> its durable,
/// strictly increasing <see cref="CloudIdentityOutboxEvent.SequenceNumber"/>, the identity/allegiance
/// analog of <see cref="CloudCustodyOutboxSequence"/> (see that type's doc comment for the exact
/// locking rationale, which applies identically here). Kept as its own singleton row rather than
/// reusing <see cref="CloudCustodyOutboxSequence"/> so the two outboxes -- custody handoffs and
/// identity/allegiance changes -- can be read, replayed, and reasoned about independently.
/// </summary>
public sealed class CloudIdentityOutboxSequence
{
    private CloudIdentityOutboxSequence()
    {
    }

    public int Id { get; private set; } = 1;

    public long NextValue { get; private set; }
}
