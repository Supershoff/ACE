using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// The opaque identity of a Cloud ownership account: a Main Account or an Allegiance Vault
/// (CONTEXT.md's "opaque Cloud ownership identity"). Never blank; never interchangeable with any
/// other shard-scoped identifier kind.
/// </summary>
[JsonConverter(typeof(CloudGuidIdJsonConverter<CloudAccountId>))]
public sealed class CloudAccountId : CloudGuidId<CloudAccountId>
{
    public CloudAccountId(Guid value)
        : base(value, "A Cloud Account ID is required and cannot be empty.")
    {
    }
}
