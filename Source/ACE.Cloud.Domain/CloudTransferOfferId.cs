using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// The identity of one Transfer Offer (XFER-001, XFER-002): a time-limited, revocable proposal to
/// transfer a reserved set of Cloud Items to another Main Account upon recipient acceptance.
/// </summary>
[JsonConverter(typeof(CloudGuidIdJsonConverter<CloudTransferOfferId>))]
public sealed class CloudTransferOfferId : CloudGuidId<CloudTransferOfferId>
{
    public CloudTransferOfferId(Guid value)
        : base(value, "A Transfer Offer ID is required and cannot be empty.")
    {
    }
}
