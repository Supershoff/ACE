namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudCustodyRecordIdTests : CloudGuidIdTestsBase<CloudCustodyRecordId>
{
    protected override CloudCustodyRecordId Create(Guid value) => new(value);
}
