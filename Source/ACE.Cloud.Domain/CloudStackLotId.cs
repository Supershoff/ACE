using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// The identity of one Cloud Stack Lot: an independently owned or reserved quantity claim against
/// a stackable biota in Cloud custody (ARCH-010, ARCH-011, INV-001).
/// </summary>
[JsonConverter(typeof(CloudGuidIdJsonConverter<CloudStackLotId>))]
public sealed class CloudStackLotId : CloudGuidId<CloudStackLotId>
{
    public CloudStackLotId(Guid value)
        : base(value, "A Cloud Stack Lot ID is required and cannot be empty.")
    {
    }
}
