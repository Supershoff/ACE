using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #17's VAULT-005 out-of-band recovery case: "An out-of-band monarch
/// deletion leaves the vault available only for audited administrator recovery" (CONTEXT.md line
/// 407). These simulate a monarch character row disappearing from ace_shard without ever routing
/// through ACE's own guarded deletion path (which would have refused it while the vault was
/// nonempty), and prove <see cref="CloudAllegianceVaultGateway.DetectOutOfBandMonarchVaultOrphansAsync"/>
/// surfaces exactly that case without guessing a successor vault.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudMonarchVaultOrphanDetectionTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 760_000;

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
    public async Task DetectOutOfBandMonarchVaultOrphansAsync_WhenMonarchRowIsGoneAndVaultIsNonempty_RecordsADiagnostic()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, monarchId, accountId: 1, name: "OldMonarch");

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        await boundary.DepositAsync(biotaId, ShardId, vaultOwnerId, Guid.NewGuid());

        // Establishes this vault's reverse-lookup binding, exactly as ACE's own guard would the
        // first time anyone ever checks this monarch's vault (for example an earlier deletion
        // attempt, blocked or not) before the out-of-band deletion below removes all trace of that.
        await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        // Simulate an out-of-band deletion: the character row disappears without ACE's own guarded
        // deletion path (which would have blocked it while the vault was nonempty) ever running.
        await AceShardTestData.DeleteCharacterRowAsync(_fixture.AceShardConnectionString, monarchId);

        var diagnostics = await vaultGateway.DetectOutOfBandMonarchVaultOrphansAsync(ShardId);

        Assert.HasCount(1, diagnostics);
        Assert.AreEqual(monarchId, diagnostics[0].MonarchCharacterId);
        Assert.AreEqual(vaultOwnerId, diagnostics[0].VaultOwnerId);

        var persisted = await context.CloudMonarchDeletionDiagnostics.AsNoTracking()
            .SingleOrDefaultAsync(d => d.MonarchCharacterId == monarchId);
        Assert.IsNotNull(persisted);
    }

    [TestMethod]
    public async Task DetectOutOfBandMonarchVaultOrphansAsync_WhenTheCharacterStillExists_FindsNothing()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, monarchId, accountId: 1, name: "StillHere");

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        await boundary.DepositAsync(biotaId, ShardId, vaultOwnerId, Guid.NewGuid());
        await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        var diagnostics = await vaultGateway.DetectOutOfBandMonarchVaultOrphansAsync(ShardId);

        Assert.HasCount(0, diagnostics);
    }

    [TestMethod]
    public async Task DetectOutOfBandMonarchVaultOrphansAsync_WhenTheVaultIsEmpty_FindsNothing_EvenIfTheCharacterIsGone()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();

        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);

        // Ensures a reverse-lookup binding exists (as GetIsEmptyAsync would create in production)
        // even though this vault never actually held anything.
        await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        var diagnostics = await vaultGateway.DetectOutOfBandMonarchVaultOrphansAsync(ShardId);

        Assert.HasCount(0, diagnostics);
    }

    [TestMethod]
    public async Task DetectOutOfBandMonarchVaultOrphansAsync_WhenSoftDeleted_StillRecordsADiagnostic()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);

        // A soft-deleted (is_Deleted = 1) row still counts as "no longer a valid monarch" for this
        // check -- ACE's guarded deletion path is what should have blocked this in the first place.
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, monarchId, accountId: 1, name: "SoftDeleted", isDeleted: true);

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        await boundary.DepositAsync(biotaId, ShardId, vaultOwnerId, Guid.NewGuid());
        await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        var diagnostics = await vaultGateway.DetectOutOfBandMonarchVaultOrphansAsync(ShardId);

        Assert.HasCount(1, diagnostics);
    }

    [TestMethod]
    public async Task DetectOutOfBandMonarchVaultOrphansAsync_WhenTheCharacterHasSwornToAnotherMonarch_RecordsADiagnostic()
    {
        // Issue #17 review, finding 2 (P1): a former monarch who swears into another allegiance still
        // exists as an ordinary vassal (not deleted), so the deleted-character check alone can never
        // catch a VAULT-004 Vault Absorption that failed or was refused. This is the state-based
        // safety net for exactly that case.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var newMonarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, monarchId, accountId: 1, name: "FormerMonarch");
        // biota_properties_i_i_d.object_Id has a foreign key to biota.id, so the character's own
        // Monarch instance property (granted below) requires its own biota row to exist too.
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, monarchId);

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        await boundary.DepositAsync(biotaId, ShardId, vaultOwnerId, Guid.NewGuid());
        await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        // Simulate the world-side half of Player.HandleMonarchSwear completing (the character now
        // points at a new monarch) even though the vault absorption that should accompany it did not.
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, monarchId, newMonarchId);

        var diagnostics = await vaultGateway.DetectOutOfBandMonarchVaultOrphansAsync(ShardId);

        Assert.HasCount(1, diagnostics);
        Assert.AreEqual(monarchId, diagnostics[0].MonarchCharacterId);
        Assert.AreEqual(vaultOwnerId, diagnostics[0].VaultOwnerId);
    }

    [TestMethod]
    public async Task DetectOutOfBandMonarchVaultOrphansAsync_WhenTheCharacterIsStillItsOwnMonarch_FindsNothing()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, monarchId, accountId: 1, name: "StillMonarch");

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        await boundary.DepositAsync(biotaId, ShardId, vaultOwnerId, Guid.NewGuid());
        await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        var diagnostics = await vaultGateway.DetectOutOfBandMonarchVaultOrphansAsync(ShardId);

        Assert.HasCount(0, diagnostics);
    }

    [TestMethod]
    public async Task DetectOutOfBandMonarchVaultOrphansAsync_IsIdempotent_NeverRecordsTheSameMonarchTwice()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        await boundary.DepositAsync(biotaId, ShardId, vaultOwnerId, Guid.NewGuid());
        await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        var firstRun = await vaultGateway.DetectOutOfBandMonarchVaultOrphansAsync(ShardId);
        var secondRun = await vaultGateway.DetectOutOfBandMonarchVaultOrphansAsync(ShardId);

        Assert.HasCount(1, firstRun, "The character row never existed in this test, so the very first scan already finds the orphan.");
        Assert.HasCount(0, secondRun, "A vault already diagnosed must never be diagnosed again.");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
