using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// The idempotency key a caller presents at a Cloud boundary transaction (ARCH-006, transaction
/// rules 4 and 8): repeating a request with the same key must replay its committed result rather
/// than reapplying the mutation. Never blank.
/// </summary>
[JsonConverter(typeof(CloudGuidIdJsonConverter<CloudIdempotencyKey>))]
public sealed class CloudIdempotencyKey : CloudGuidId<CloudIdempotencyKey>
{
    public CloudIdempotencyKey(Guid value)
        : base(value, "An idempotency key is required and cannot be empty.")
    {
    }
}
