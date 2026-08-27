namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudReservationIdTests : CloudGuidIdTestsBase<CloudReservationId>
{
    protected override CloudReservationId Create(Guid value) => new(value);
}
