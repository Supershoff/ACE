namespace ACE.Cloud.Domain;

/// <summary>
/// The lifecycle state of one <see cref="CloudReservation"/> or <see cref="CloudReservationAllocation"/>.
/// </summary>
public enum CloudReservationStatus
{
    /// <summary>The reservation currently exclusively holds its target(s).</summary>
    Active,

    /// <summary>The reservation has ended; its target(s) are free for a new reservation.</summary>
    Released,
}
