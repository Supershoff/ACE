namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudAccountIdTests : CloudGuidIdTestsBase<CloudAccountId>
{
    protected override CloudAccountId Create(Guid value) => new(value);
}
