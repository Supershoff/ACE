using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// The identity of one Cloud Custody Record (ARCH-005, INV-001).
/// </summary>
[JsonConverter(typeof(CloudGuidIdJsonConverter<CloudCustodyRecordId>))]
public sealed class CloudCustodyRecordId : CloudGuidId<CloudCustodyRecordId>
{
    public CloudCustodyRecordId(Guid value)
        : base(value, "A Cloud Custody Record ID is required and cannot be empty.")
    {
    }
}
