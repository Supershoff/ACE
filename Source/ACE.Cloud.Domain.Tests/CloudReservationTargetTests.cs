namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudReservationTargetTests
{
    [TestMethod]
    public void ForItem_RejectsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CloudReservationTarget.ForItem(null!));
    }

    [TestMethod]
    public void ForStackLot_RejectsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CloudReservationTarget.ForStackLot(null!));
    }

    [TestMethod]
    public void Equality_IsValueBasedOnKindAndUnderlyingId()
    {
        var itemId = new CloudItemId(12345);
        var first = CloudReservationTarget.ForItem(itemId);
        var second = CloudReservationTarget.ForItem(new CloudItemId(12345));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Equality_AnItemTargetNeverEqualsAStackLotTarget()
    {
        var lotId = Guid.NewGuid();
        var itemTarget = CloudReservationTarget.ForItem(new CloudItemId(1));
        var lotTarget = CloudReservationTarget.ForStackLot(new CloudStackLotId(lotId));

        Assert.AreNotEqual(itemTarget, lotTarget);
    }

    [TestMethod]
    public void ToString_DescribesTheTargetKind()
    {
        var itemTarget = CloudReservationTarget.ForItem(new CloudItemId(42));
        var lotTarget = CloudReservationTarget.ForStackLot(new CloudStackLotId(Guid.NewGuid()));

        StringAssert.Contains(itemTarget.ToString(), "Item");
        StringAssert.Contains(lotTarget.ToString(), "Stack Lot");
    }
}
