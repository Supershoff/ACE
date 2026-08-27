namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Shared contract tests for every <see cref="CloudGuidId{TSelf}"/>-derived identifier: it must
/// reject an empty Guid, compare by value, and never compare equal to a differently typed Guid
/// identifier even when both wrap the same underlying value.
/// </summary>
public abstract class CloudGuidIdTestsBase<T>
    where T : CloudGuidId<T>
{
    protected abstract T Create(Guid value);

    [TestMethod]
    public void Constructor_RejectsEmptyGuid()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Create(Guid.Empty));
    }

    [TestMethod]
    public void Equality_IsValueBased()
    {
        var value = Guid.NewGuid();
        var first = Create(value);
        var second = Create(value);
        var different = Create(Guid.NewGuid());

        Assert.AreEqual(first, second);
        Assert.IsTrue(first == second);
        Assert.AreNotEqual(first, different);
        Assert.IsTrue(first != different);
    }

    [TestMethod]
    public void ToString_ReturnsUnderlyingGuid()
    {
        var value = Guid.NewGuid();
        var id = Create(value);

        Assert.AreEqual(value.ToString(), id.ToString());
    }
}
