using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// AC Cloud Mule issue #20's Red section against a real MariaDB: "Test highest-total-logins
/// default, selection, rename/deletion fallback, immutable snapshots, and no-current-character
/// behavior" (AUTH-003), persisted this time rather than the pure policy tests in
/// ACE.Cloud.Domain.Tests.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudDisplayCharacterGatewayTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;

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

    private static async Task<Guid> CreateOwnershipGroupAsync(CloudDbContext context, uint mainAccountId)
    {
        var group = new CloudOwnershipGroup(ShardId, mainAccountId);
        context.CloudOwnershipGroups.Add(group);
        await context.SaveChangesAsync();
        return group.Id;
    }

    [TestMethod]
    public async Task ReselectAsync_FirstSelection_PersistsTheHighestTotalLoginsCandidate()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        Guid groupId;
        await using (var setupContext = new CloudDbContext(options))
        {
            groupId = await CreateOwnershipGroupAsync(setupContext, mainAccountId: 1);
        }

        await using var context = new CloudDbContext(options);
        var gateway = new CloudDisplayCharacterGateway(context);

        var candidates = new[]
        {
            new CloudDisplayCharacterCandidate(1, "Alt", totalLogins: 5),
            new CloudDisplayCharacterCandidate(2, "Main", totalLogins: 500),
        };

        var result = await gateway.ReselectAsync(ShardId, groupId, candidates, CloudDisplayCharacterSelectionReason.InitialSelection, Guid.NewGuid());

        Assert.IsTrue(result.HasSelection);
        Assert.AreEqual("Main", result.CharacterName);

        await using var verifyContext = new CloudDbContext(options);
        var persisted = await new CloudDisplayCharacterGateway(verifyContext).GetCurrentSelectionAsync(groupId);
        Assert.IsNotNull(persisted);
        Assert.AreEqual("Main", persisted!.CharacterName);
        Assert.AreEqual(2u, persisted.CharacterId);
    }

    [TestMethod]
    public async Task ReselectAsync_AfterTheSelectedCharacterIsDeleted_FallsBackAndAppendsHistory()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        Guid groupId;
        await using (var setupContext = new CloudDbContext(options))
        {
            groupId = await CreateOwnershipGroupAsync(setupContext, mainAccountId: 2);
        }

        await using (var initialContext = new CloudDbContext(options))
        {
            var initialCandidates = new[]
            {
                new CloudDisplayCharacterCandidate(1, "WasWinner", totalLogins: 500),
                new CloudDisplayCharacterCandidate(2, "RemainingAlt", totalLogins: 20),
            };
            await new CloudDisplayCharacterGateway(initialContext)
                .ReselectAsync(ShardId, groupId, initialCandidates, CloudDisplayCharacterSelectionReason.InitialSelection, Guid.NewGuid());
        }

        await using var context = new CloudDbContext(options);
        var afterDeletion = await new CloudDisplayCharacterGateway(context).ReselectAsync(
            ShardId,
            groupId,
            [new CloudDisplayCharacterCandidate(2, "RemainingAlt", totalLogins: 20)],
            CloudDisplayCharacterSelectionReason.CharacterDeleted,
            Guid.NewGuid());

        Assert.AreEqual(2u, afterDeletion.CharacterId);
        Assert.AreEqual("RemainingAlt", afterDeletion.CharacterName);

        await using var verifyContext = new CloudDbContext(options);
        var historyCount = await verifyContext.CloudDisplayCharacterSelectionHistoryEvents.CountAsync(e => e.OwnershipGroupId == groupId);
        Assert.AreEqual(2, historyCount, "Both the initial selection and the fallback reselection must be recorded.");
    }

    [TestMethod]
    public async Task ReselectAsync_NoCurrentCharacterInTheGroup_PersistsNoSelection()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        Guid groupId;
        await using (var setupContext = new CloudDbContext(options))
        {
            groupId = await CreateOwnershipGroupAsync(setupContext, mainAccountId: 3);
        }

        await using var context = new CloudDbContext(options);
        var result = await new CloudDisplayCharacterGateway(context)
            .ReselectAsync(ShardId, groupId, [], CloudDisplayCharacterSelectionReason.RosterChanged, Guid.NewGuid());

        Assert.IsFalse(result.HasSelection);

        await using var verifyContext = new CloudDbContext(options);
        var persisted = await new CloudDisplayCharacterGateway(verifyContext).GetCurrentSelectionAsync(groupId);
        Assert.IsNotNull(persisted);
        Assert.IsNull(persisted!.CharacterId);
        Assert.IsNull(persisted.CharacterName);
    }

    [TestMethod]
    public async Task GetCurrentSelectionAsync_NeverSelected_ReturnsNull()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        Guid groupId;
        await using (var setupContext = new CloudDbContext(options))
        {
            groupId = await CreateOwnershipGroupAsync(setupContext, mainAccountId: 4);
        }

        await using var context = new CloudDbContext(options);
        var persisted = await new CloudDisplayCharacterGateway(context).GetCurrentSelectionAsync(groupId);

        Assert.IsNull(persisted);
    }
}
