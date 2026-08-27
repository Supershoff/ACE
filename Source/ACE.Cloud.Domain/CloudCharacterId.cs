using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// The immutable identity of an ACE character used as an Acting Character or a withdrawal
/// recipient, independent of that character's current name (AUTH-003, VAULT-001).
/// </summary>
[JsonConverter(typeof(CloudGuidIdJsonConverter<CloudCharacterId>))]
public sealed class CloudCharacterId : CloudGuidId<CloudCharacterId>
{
    public CloudCharacterId(Guid value)
        : base(value, "A Cloud Character ID is required and cannot be empty.")
    {
    }
}
