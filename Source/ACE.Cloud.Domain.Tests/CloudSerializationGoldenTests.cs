using System.Text.Json;
using ACE.Cloud.Contracts;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Golden serialization tests (issue #6 Red section) for representative commands, results,
/// ledger events, outbox events, and Live State Stream envelopes. Each test proves serializing a
/// fixed instance twice produces byte-identical JSON, and that deserializing and reserializing
/// reproduces that exact JSON and an equal object, satisfying the acceptance criterion "Contracts
/// round-trip deterministically."
/// </summary>
[TestClass]
public sealed class CloudSerializationGoldenTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly CloudShardId ShardId = new("us1");
    private static readonly DateTimeOffset FixedInstant = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void CommandEnvelope_RoundTripsDeterministically()
    {
        var envelope = new CloudCommandEnvelope<CloudWithdrawalReservationCommand>(
            new CloudProtocolHandshake(ShardId, new CloudComponentVersions("1.2.0", "0.3.0", "2.0.0")),
            new CloudIdempotencyKey(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            new CloudActorIdentity(CloudActorKind.Character, Guid.Parse("44444444-4444-4444-4444-444444444444"), "Aerbax"),
            new CloudWithdrawalReservationCommand(
                new CloudItemId(123456789),
                new CloudAccountId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                new CloudReservationId(Guid.Parse("22222222-2222-2222-2222-222222222222"))),
            FixedInstant,
            new CloudAggregateVersion(4));

        AssertRoundTripsDeterministically(envelope);
    }

    [TestMethod]
    public void CommandResult_EachKind_RoundTripsDeterministically()
    {
        AssertRoundTripsDeterministically(CloudCommandResult<string>.Success("committed"));
        AssertRoundTripsDeterministically(CloudCommandResult<string>.IdempotentReplay("committed"));
        AssertRoundTripsDeterministically(CloudCommandResult<string>.Conflict("stale version"));
        AssertRoundTripsDeterministically(CloudCommandResult<string>.ValidationFailed("not eligible"));
        AssertRoundTripsDeterministically(CloudCommandResult<string>.Unavailable("ACE world process is offline"));
    }

    [TestMethod]
    public void ActivityLedgerEvent_RoundTripsDeterministically()
    {
        var envelope = new CloudEventEnvelope<CloudActivityLedgerEventPayload>(
            ShardId,
            new CloudAggregateVersion(2),
            new CloudIdempotencyKey(Guid.Parse("55555555-5555-5555-5555-555555555555")),
            FixedInstant,
            new CloudActivityLedgerEventPayload(
                CloudActorIdentity.SystemActor("Withdrawal Token Expiry Sweep"),
                new CloudAccountId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                new CloudItemId(123456789),
                eventKind: "WithdrawalTokenExpired",
                outcome: "Committed"));

        AssertRoundTripsDeterministically(envelope);
    }

    [TestMethod]
    public void CustodyOutboxEvent_RoundTripsDeterministically()
    {
        var envelope = new CloudEventEnvelope<CloudCustodyOutboxEventPayload>(
            ShardId,
            new CloudAggregateVersion(1),
            new CloudIdempotencyKey(Guid.Parse("66666666-6666-6666-6666-666666666666")),
            FixedInstant,
            new CloudCustodyOutboxEventPayload(
                new CloudItemId(123456789),
                new CloudAccountId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                eventKind: "Deposit"));

        AssertRoundTripsDeterministically(envelope);
    }

    [TestMethod]
    public void LiveStateStreamEnvelope_RoundTripsDeterministically()
    {
        var envelope = new CloudPublicEventEnvelope<CloudListingPublicSnapshot>(
            ShardId,
            new CloudAggregateVersion(7),
            eventKind: "ListingPublished",
            FixedInstant,
            new CloudListingPublicSnapshot(ShardId, sellerDisplayCharacter: "Aerbax", currentPriceUnits: 500));

        AssertRoundTripsDeterministically(envelope);
    }

    private static void AssertRoundTripsDeterministically<T>(T value)
    {
        var first = JsonSerializer.Serialize(value, Options);
        var second = JsonSerializer.Serialize(value, Options);

        Assert.AreEqual(first, second, "Serializing the same contract twice must produce identical JSON.");

        var roundTripped = JsonSerializer.Deserialize<T>(first, Options);

        Assert.AreEqual(value, roundTripped);

        var reserialized = JsonSerializer.Serialize(roundTripped, Options);

        Assert.AreEqual(first, reserialized, "Deserializing and reserializing must reproduce the original JSON exactly.");
    }
}
