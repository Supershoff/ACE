namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// A <see cref="CloudReservation"/> is an immutable aggregate: construction fixes its identity and
/// starting state, and every transition (<see cref="CloudReservation.Released"/>, exercised through
/// <see cref="CloudReservationPolicy.Release"/>) returns a new instance rather than mutating the
/// original (ARCH-006, transaction rule 3).
/// </summary>
[TestClass]
public sealed class CloudReservationTests
{
    private static readonly CloudReservationId ReservationId = new(Guid.NewGuid());
    private static readonly CloudAccountId OwnerId = new(Guid.NewGuid());
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [TestMethod]
    public void Constructor_StartsActiveAtInitialVersion()
    {
        var reservation = new CloudReservation(ReservationId, CloudReservationKind.Withdrawal, OwnerId, CreatedAt, expiresAtUtc: null);

        Assert.AreEqual(CloudReservationStatus.Active, reservation.Status);
        Assert.AreEqual(CloudAggregateVersion.Initial, reservation.Version);
        Assert.IsNull(reservation.ReleasedAtUtc);
        Assert.IsNull(reservation.ReleaseReason);
    }

    [TestMethod]
    public void Constructor_RejectsExpiryAtOrBeforeCreation()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new CloudReservation(ReservationId, CloudReservationKind.Withdrawal, OwnerId, CreatedAt, expiresAtUtc: CreatedAt));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new CloudReservation(ReservationId, CloudReservationKind.Withdrawal, OwnerId, CreatedAt, expiresAtUtc: CreatedAt.AddSeconds(-1)));
    }

    [TestMethod]
    public void IsExpiredAt_NullExpiry_NeverExpires()
    {
        var reservation = new CloudReservation(ReservationId, CloudReservationKind.Withdrawal, OwnerId, CreatedAt, expiresAtUtc: null);

        Assert.IsFalse(reservation.IsExpiredAt(CreatedAt.AddYears(100)));
    }

    [TestMethod]
    public void IsExpiredAt_ComparesAgainstTheSuppliedDatabaseTime()
    {
        var expiresAt = CreatedAt.AddMinutes(15);
        var reservation = new CloudReservation(ReservationId, CloudReservationKind.Withdrawal, OwnerId, CreatedAt, expiresAt);

        Assert.IsFalse(reservation.IsExpiredAt(expiresAt.AddSeconds(-1)));
        Assert.IsTrue(reservation.IsExpiredAt(expiresAt));
        Assert.IsTrue(reservation.IsExpiredAt(expiresAt.AddSeconds(1)));
    }

    [TestMethod]
    public void Released_ReturnsANewInstanceAndDoesNotMutateTheOriginal()
    {
        var reservation = new CloudReservation(ReservationId, CloudReservationKind.Withdrawal, OwnerId, CreatedAt, expiresAtUtc: null);
        var releasedAt = CreatedAt.AddMinutes(1);

        var released = reservation.Released(releasedAt, CloudReservationReleaseReason.Cancelled);

        Assert.AreEqual(CloudReservationStatus.Active, reservation.Status);
        Assert.AreEqual(CloudAggregateVersion.Initial, reservation.Version);

        Assert.AreEqual(CloudReservationStatus.Released, released.Status);
        Assert.AreEqual(CloudAggregateVersion.Initial.Next(), released.Version);
        Assert.AreEqual(releasedAt, released.ReleasedAtUtc);
        Assert.AreEqual(CloudReservationReleaseReason.Cancelled, released.ReleaseReason);
        Assert.AreEqual(reservation.Id, released.Id);
        Assert.AreEqual(reservation.Kind, released.Kind);
        Assert.AreEqual(reservation.OwnerId, released.OwnerId);
    }
}
