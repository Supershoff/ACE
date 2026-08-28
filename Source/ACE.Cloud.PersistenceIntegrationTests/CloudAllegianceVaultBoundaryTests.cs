using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #17's Allegiance Vault emptiness check and Vault Absorption
/// (VAULT-001, VAULT-004, VAULT-005). A vault is modeled as an ordinary Cloud ownership identity
/// (<see cref="CloudOwnerIdentity.ForAllegianceVault"/>), so these tests exercise
/// <see cref="CloudAllegianceVaultGateway"/> through the exact same
/// <see cref="CloudCustodyBoundary"/> deposit/lot machinery every other owner uses.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudAllegianceVaultBoundaryTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 750_000;

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
    public async Task GetIsEmptyAsync_ForAMonarchWithNoActivity_IsTrue()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);

        var isEmpty = await vaultGateway.GetIsEmptyAsync(ShardId, NextId());

        Assert.IsTrue(isEmpty);
    }

    [TestMethod]
    public async Task GetIsEmptyAsync_AfterAWholeItemDeposit_IsFalse()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        var deposit = await boundary.DepositAsync(biotaId, ShardId, vaultOwnerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, deposit.Kind);

        var isEmpty = await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        Assert.IsFalse(isEmpty);
    }

    [TestMethod]
    public async Task GetIsEmptyAsync_AfterAStackLotOwnedByTheVault_IsFalse()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        var deposit = await boundary.DepositStackAsync(biotaId, ShardId, vaultOwnerId, quantity: 5, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, deposit.Kind);

        var isEmpty = await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        Assert.IsFalse(isEmpty);
    }

    [TestMethod]
    public async Task GetIsEmptyAsync_CreatesAReverseLookupBinding_EvenWhenEmpty()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();

        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        var binding = await context.CloudAllegianceVaultBindings.AsNoTracking()
            .SingleOrDefaultAsync(b => b.ShardId == ShardId && b.MonarchCharacterId == monarchId);

        Assert.IsNotNull(binding, "A vault's reverse-lookup binding must exist once its emptiness has ever been checked, so a later integrity scan can find it even if it never held anything.");
    }

    [TestMethod]
    public async Task AbsorbAsync_MovesWholeItemsAndStackLots_FromSourceToDestination()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var oldMonarchId = NextId();
        var newMonarchId = NextId();
        var oldVaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, oldMonarchId);
        var newVaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, newMonarchId);

        var wholeItemBiotaId = NextId();
        var stackBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, wholeItemBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, stackBiotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        await boundary.DepositAsync(wholeItemBiotaId, ShardId, oldVaultOwnerId, Guid.NewGuid());
        await boundary.DepositStackAsync(stackBiotaId, ShardId, oldVaultOwnerId, quantity: 10, Guid.NewGuid());

        var outcome = await vaultGateway.AbsorbAsync(ShardId, oldMonarchId, newMonarchId);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.AreEqual(1, outcome.Value!.CustodyRecordsMoved);
        Assert.AreEqual(1, outcome.Value!.StackLotsMoved);

        Assert.IsTrue(await vaultGateway.GetIsEmptyAsync(ShardId, oldMonarchId), "The former monarch's vault must be empty after absorption.");
        Assert.IsFalse(await vaultGateway.GetIsEmptyAsync(ShardId, newMonarchId), "The new monarch's vault must now hold the absorbed contents.");

        var custodyRecord = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == wholeItemBiotaId);
        Assert.AreEqual(newVaultOwnerId, custodyRecord.OwnerId);

        var lot = await context.CloudStackLots.AsNoTracking().SingleAsync();
        Assert.AreEqual(newVaultOwnerId, lot.OwnerId);
    }

    [TestMethod]
    public async Task AbsorbAsync_OnAnAlreadyEmptySourceVault_SucceedsAsANoOp()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);

        var outcome = await vaultGateway.AbsorbAsync(ShardId, NextId(), NextId());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.AreEqual(0, outcome.Value!.TotalItemsMoved);
    }

    [TestMethod]
    public async Task AbsorbAsync_WithTheSameMonarchAsSourceAndDestination_IsAConflict()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();

        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);

        var outcome = await vaultGateway.AbsorbAsync(ShardId, monarchId, monarchId);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
