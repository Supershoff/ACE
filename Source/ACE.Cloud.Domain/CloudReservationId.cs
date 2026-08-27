using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// The identity of one exclusive Cloud reservation: a Withdrawal Reservation, Listing Reservation,
/// Transfer Offer hold, or Bid Escrow allocation. One quantity may have at most one exclusive
/// reservation at a time (IMPLEMENTATION-BRIEF.md's core custody state model).
/// </summary>
[JsonConverter(typeof(CloudGuidIdJsonConverter<CloudReservationId>))]
public sealed class CloudReservationId : CloudGuidId<CloudReservationId>
{
    public CloudReservationId(Guid value)
        : base(value, "A Cloud Reservation ID is required and cannot be empty.")
    {
    }
}
