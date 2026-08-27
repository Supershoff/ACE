namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudStackLotIdTests : CloudGuidIdTestsBase<CloudStackLotId>
{
    protected override CloudStackLotId Create(Guid value) => new(value);
}
