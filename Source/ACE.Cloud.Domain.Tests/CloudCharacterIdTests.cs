namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudCharacterIdTests : CloudGuidIdTestsBase<CloudCharacterId>
{
    protected override CloudCharacterId Create(Guid value) => new(value);
}
