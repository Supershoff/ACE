using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// The identity of one personal Sharing Grant (SHARE-001..004): one owner's permission assignment
/// to one resolved grantee Main/Linked ownership group. Distinct at compile time from every other
/// opaque Guid identifier kind (<see cref="CloudGuidId{TSelf}"/>'s own rationale).
/// </summary>
[JsonConverter(typeof(CloudGuidIdJsonConverter<CloudSharingGrantId>))]
public sealed class CloudSharingGrantId : CloudGuidId<CloudSharingGrantId>
{
    public CloudSharingGrantId(Guid value)
        : base(value, "A Cloud Sharing Grant ID is required and cannot be empty.")
    {
    }
}
