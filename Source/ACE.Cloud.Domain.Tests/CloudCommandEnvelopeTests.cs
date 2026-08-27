using ACE.Cloud.Contracts;

namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudCommandEnvelopeTests
{
    private static readonly CloudProtocolHandshake ValidHandshake = new(
        new CloudShardId("us1"),
        new CloudComponentVersions("1.2.0", "0.3.0", "2.0.0"));

    private static readonly CloudIdempotencyKey ValidIdempotencyKey = new(Guid.NewGuid());

    private static readonly CloudActorIdentity ValidActor = CloudActorIdentity.SystemActor("Test");

    private const string ValidCommand = "command-payload";

    [TestMethod]
    public void Constructor_RejectsNullHandshake()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new CloudCommandEnvelope<string>(
            null!, ValidIdempotencyKey, ValidActor, ValidCommand, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void Constructor_RejectsNullIdempotencyKey()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new CloudCommandEnvelope<string>(
            ValidHandshake, null!, ValidActor, ValidCommand, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void Constructor_RejectsNullActor()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new CloudCommandEnvelope<string>(
            ValidHandshake, ValidIdempotencyKey, null!, ValidCommand, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void Constructor_RejectsNullCommand()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new CloudCommandEnvelope<string>(
            ValidHandshake, ValidIdempotencyKey, ValidActor, null!, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void Constructor_DefaultsExpectedVersionToNullForCreationCommands()
    {
        var envelope = new CloudCommandEnvelope<string>(
            ValidHandshake, ValidIdempotencyKey, ValidActor, ValidCommand, DateTimeOffset.UtcNow);

        Assert.IsNull(envelope.ExpectedVersion);
    }

    [TestMethod]
    public void Constructor_CarriesEveryField()
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var expectedVersion = new CloudAggregateVersion(2);

        var envelope = new CloudCommandEnvelope<string>(
            ValidHandshake, ValidIdempotencyKey, ValidActor, ValidCommand, issuedAt, expectedVersion);

        Assert.AreEqual(ValidHandshake, envelope.Handshake);
        Assert.AreEqual(ValidIdempotencyKey, envelope.IdempotencyKey);
        Assert.AreEqual(ValidActor, envelope.Actor);
        Assert.AreEqual(ValidCommand, envelope.Command);
        Assert.AreEqual(issuedAt, envelope.IssuedAtUtc);
        Assert.AreEqual(expectedVersion, envelope.ExpectedVersion);
    }
}
