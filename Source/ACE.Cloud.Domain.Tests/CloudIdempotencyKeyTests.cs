namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// ARCH-006, transaction rule 4: an absent idempotency key must be rejected before a boundary
/// transaction can be attempted.
/// </summary>
[TestClass]
public sealed class CloudIdempotencyKeyTests : CloudGuidIdTestsBase<CloudIdempotencyKey>
{
    protected override CloudIdempotencyKey Create(Guid value) => new(value);
}
