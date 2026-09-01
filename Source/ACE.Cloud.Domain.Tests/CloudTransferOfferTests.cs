namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Aggregate-shape invariants for <see cref="CloudTransferOffer"/> (issue #35, XFER-001, XFER-002).
/// </summary>
[TestClass]
public sealed class CloudTransferOfferTests
{
    private static readonly CloudTransferOfferId Id = new(Guid.NewGuid());
    private static readonly CloudAccountId SenderId = new(Guid.NewGuid());
    private static readonly CloudAccountId RecipientId = new(Guid.NewGuid());
    private static readonly CloudReservationId ReservationId = new(Guid.NewGuid());
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [TestMethod]
    public void Constructor_ANewOffer_StartsPendingAtInitialVersionWithNoResolution()
    {
        var offer = new CloudTransferOffer(Id, SenderId, RecipientId, ReservationId, CreatedAtUtc, CreatedAtUtc.AddDays(7));

        Assert.AreEqual(CloudTransferOfferStatus.Pending, offer.Status);
        Assert.AreEqual(CloudAggregateVersion.Initial, offer.Version);
        Assert.IsNull(offer.ResolvedAtUtc);
    }

    [TestMethod]
    public void Constructor_WithTheSameSenderAndRecipient_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CloudTransferOffer(Id, SenderId, SenderId, ReservationId, CreatedAtUtc, CreatedAtUtc.AddDays(7)));
    }

    [TestMethod]
    public void Constructor_WithAnExpiryAtOrBeforeCreation_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new CloudTransferOffer(Id, SenderId, RecipientId, ReservationId, CreatedAtUtc, CreatedAtUtc));
    }

    [TestMethod]
    public void IsExpiredAt_BeforeTheDeadline_IsFalse()
    {
        var offer = new CloudTransferOffer(Id, SenderId, RecipientId, ReservationId, CreatedAtUtc, CreatedAtUtc.AddDays(7));

        Assert.IsFalse(offer.IsExpiredAt(CreatedAtUtc.AddDays(6)));
    }

    [TestMethod]
    public void IsExpiredAt_AtOrAfterTheDeadline_IsTrue()
    {
        var offer = new CloudTransferOffer(Id, SenderId, RecipientId, ReservationId, CreatedAtUtc, CreatedAtUtc.AddDays(7));

        Assert.IsTrue(offer.IsExpiredAt(CreatedAtUtc.AddDays(7)));
        Assert.IsTrue(offer.IsExpiredAt(CreatedAtUtc.AddDays(8)));
    }
}
