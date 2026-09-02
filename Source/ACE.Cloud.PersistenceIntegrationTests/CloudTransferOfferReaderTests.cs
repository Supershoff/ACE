using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #39's Transfer Offer list reader (XFER-001, XFER-002): the web "sent"/"received" views each
/// see only their own side, and each summary carries every target the offer actually locked.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudTransferOfferReaderTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 650_000;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _fixture = await CloudDatabaseFixture.StartAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _fixture.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await CloudBoundaryTestFixtureData.ResetAsync(_fixture.CloudConnectionString, ShardId);
    }

    [TestMethod]
    public async Task GetSentAndGetReceivedAsync_ReturnOnlyEachSidesOwnOffers_WithTargets()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var otherAccountId = NextId();
        var recipientCharacterId = NextId();
        var biotaId = NextId();
        const string recipientCharacterName = "ReaderRecipient";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);
        var recipientOwnerId = CloudOwnerIdentity.ForAccount(ShardId, recipientAccountId);

        await using (var context = new CloudDbContext(options))
        {
            await new CloudCustodyBoundary(context).DepositAsync(biotaId, ShardId, senderOwnerId, Guid.NewGuid());
            var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));
            var createOutcome = await gateway.CreateAsync(
                ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, createOutcome.Kind, createOutcome.Reason);
        }

        await using var readContext = new CloudDbContext(options);
        var reader = new CloudTransferOfferReader(readContext);

        var sent = await reader.GetSentAsync(ShardId, senderOwnerId);
        Assert.HasCount(1, sent);
        Assert.HasCount(1, sent[0].Targets);
        Assert.AreEqual(biotaId, sent[0].Targets[0].ItemBiotaId);

        var received = await reader.GetReceivedAsync(ShardId, recipientOwnerId);
        Assert.HasCount(1, received);

        var otherOwnerId = CloudOwnerIdentity.ForAccount(ShardId, otherAccountId);
        Assert.HasCount(0, await reader.GetSentAsync(ShardId, otherOwnerId));
        Assert.HasCount(0, await reader.GetReceivedAsync(ShardId, otherOwnerId));
        Assert.HasCount(0, await reader.GetReceivedAsync(ShardId, senderOwnerId), "The sender must never see its own sent offer under GetReceivedAsync.");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
