namespace ACE.Cloud.Domain;

/// <summary>
/// One accepted bid's committed maximum and server commit order (MKT-104, MKT-105): the private
/// maximum the marketplace may spend on the bidder's behalf, and the authoritative sequence number
/// assigned at commit time -- never browser time (MKT-105: "Use database/authority commit order,
/// never browser time").
/// </summary>
public sealed record CloudBidCommitment
{
    public CloudAccountId BidderAccountId { get; }

    public long MaxUnits { get; }

    /// <summary>Monotonically increasing authoritative commit order; a lower value commits earlier.</summary>
    public long CommitSequence { get; }

    public CloudBidCommitment(CloudAccountId bidderAccountId, long maxUnits, long commitSequence)
    {
        ArgumentNullException.ThrowIfNull(bidderAccountId);

        if (maxUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUnits), "A bid maximum must be positive.");
        }

        if (commitSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commitSequence), "A commit sequence cannot be negative.");
        }

        BidderAccountId = bidderAccountId;
        MaxUnits = maxUnits;
        CommitSequence = commitSequence;
    }
}
