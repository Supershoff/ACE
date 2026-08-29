using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// AC Cloud Mule issue #20's Red section against a real MariaDB: "standalone-source rules,
/// trees/group merges, pending obligations..., concurrent link/unlink/deposit, and retry" and
/// "unlinking changes only future routing" (AUTH-005, AUTH-006, AUTH-009). Proves the
/// <see cref="CloudActiveAccountLinkMarker"/> unique primary key -- not merely an application-level
/// check -- is what serializes a concurrent double-link race.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudAccountLinkGatewayTests
{
    private const string ShardId = "us1";
    private const uint MainAccountId = 100;
    private const uint SourceAccountId = 200;

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 800_000;

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
    public async Task LinkAsync_AStandaloneSource_IsApprovedAndTransfersEveryExistingCloudAsset()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var sourceOwnerId = CloudOwnerIdentity.ForAccount(ShardId, SourceAccountId);
        var mainOwnerId = CloudOwnerIdentity.ForAccount(ShardId, MainAccountId);

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var depositContext = new CloudDbContext(options))
        {
            var depositOutcome = await new CloudCustodyBoundary(depositContext)
                .DepositAsync(biotaId, ShardId, sourceOwnerId, Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);
        }

        await using var context = new CloudDbContext(options);
        var gateway = new CloudAccountLinkGateway(context);

        var outcome = await gateway.LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());

        Assert.IsTrue(outcome.IsApproved);
        Assert.IsNotNull(outcome.AccountLinkId);

        await using var verifyContext = new CloudDbContext(options);
        var record = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(mainOwnerId, record.OwnerId, "Linking must transfer the source account's existing Cloud asset to the Main Account.");
    }

    [TestMethod]
    public async Task LinkAsync_RepeatedWithTheSameIdempotencyKey_ReplaysTheOriginalResultInsteadOfLinkingTwice()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var idempotencyKey = Guid.NewGuid();

        await using var firstContext = new CloudDbContext(options);
        var firstOutcome = await new CloudAccountLinkGateway(firstContext).LinkAsync(ShardId, MainAccountId, SourceAccountId, idempotencyKey);
        Assert.IsTrue(firstOutcome.IsApproved);

        await using var secondContext = new CloudDbContext(options);
        var secondOutcome = await new CloudAccountLinkGateway(secondContext).LinkAsync(ShardId, MainAccountId, SourceAccountId, idempotencyKey);

        Assert.IsTrue(secondOutcome.IsApproved);
        Assert.AreEqual(firstOutcome.AccountLinkId, secondOutcome.AccountLinkId);

        await using var verifyContext = new CloudDbContext(options);
        var linkCount = await verifyContext.CloudAccountLinks.CountAsync(l => l.LinkedAccountId == SourceAccountId);
        Assert.AreEqual(1, linkCount, "A repeated idempotency key must never create a second link.");
    }

    [TestMethod]
    public async Task LinkAsync_SourceAlreadyActivelyLinkedToADifferentMain_IsRejected()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        const uint otherMainAccountId = 300;

        await using (var firstContext = new CloudDbContext(options))
        {
            var firstOutcome = await new CloudAccountLinkGateway(firstContext).LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());
            Assert.IsTrue(firstOutcome.IsApproved);
        }

        await using var secondContext = new CloudDbContext(options);
        var secondOutcome = await new CloudAccountLinkGateway(secondContext)
            .LinkAsync(ShardId, otherMainAccountId, SourceAccountId, Guid.NewGuid());

        Assert.IsFalse(secondOutcome.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.SourceAlreadyLinked, secondOutcome.RejectionCode);
    }

    [TestMethod]
    public async Task LinkAsync_SourceIsItselfAMainWithAnActiveChild_IsRejected()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        const uint sourcesOwnChildAccountId = 400;

        await using (var firstContext = new CloudDbContext(options))
        {
            // SourceAccountId becomes a Main Account with its own child.
            var firstOutcome = await new CloudAccountLinkGateway(firstContext)
                .LinkAsync(ShardId, SourceAccountId, sourcesOwnChildAccountId, Guid.NewGuid());
            Assert.IsTrue(firstOutcome.IsApproved);
        }

        await using var secondContext = new CloudDbContext(options);
        var secondOutcome = await new CloudAccountLinkGateway(secondContext)
            .LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());

        Assert.IsFalse(secondOutcome.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.SourceHasLinkedAccounts, secondOutcome.RejectionCode);
    }

    [TestMethod]
    public async Task LinkAsync_ProposedMainIsItselfAnActiveLinkedAccountElsewhere_IsRejected()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        const uint realMainAccountId = 500;
        const uint newSourceAccountId = 600;

        await using (var firstContext = new CloudDbContext(options))
        {
            // MainAccountId becomes an active Linked Account of realMainAccountId.
            var firstOutcome = await new CloudAccountLinkGateway(firstContext)
                .LinkAsync(ShardId, realMainAccountId, MainAccountId, Guid.NewGuid());
            Assert.IsTrue(firstOutcome.IsApproved);
        }

        await using var secondContext = new CloudDbContext(options);
        var secondOutcome = await new CloudAccountLinkGateway(secondContext)
            .LinkAsync(ShardId, MainAccountId, newSourceAccountId, Guid.NewGuid());

        Assert.IsFalse(secondOutcome.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.MainAccountIsLinkedElsewhere, secondOutcome.RejectionCode);
    }

    [TestMethod]
    public async Task LinkAsync_SourceHasAnActiveWithdrawalReservation_IsRejectedAsAPendingObligation()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var sourceOwnerId = CloudOwnerIdentity.ForAccount(ShardId, SourceAccountId);

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var setupContext = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(setupContext);
            var depositOutcome = await boundary.DepositAsync(biotaId, ShardId, sourceOwnerId, Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);

            var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
                biotaId, ShardId, sourceOwnerId, Convert.ToHexString(Guid.NewGuid().ToByteArray()), TimeSpan.FromMinutes(15), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind);
        }

        await using var context = new CloudDbContext(options);
        var outcome = await new CloudAccountLinkGateway(context).LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());

        Assert.IsFalse(outcome.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.SourceHasPendingObligations, outcome.RejectionCode);
    }

    [TestMethod]
    public async Task LinkAsync_AWithdrawalReservationCommitsMidTransaction_TheLinkIsRejectedAndTheReservationSurvivesUnaffected()
    {
        // AC Cloud Mule review of PR #120, finding [P1]: LinkAsync used to check pending obligations
        // with a plain, unlocked read, then reassign ownership with a raw bulk UPDATE that never
        // re-checked or locked the affected rows. A Withdrawal Reservation opened and committed in
        // that window was silently orphaned by the reassignment: the reservation kept pointing at the
        // source account while the custody record it exclusively held moved to the Main Account.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var sourceOwnerId = CloudOwnerIdentity.ForAccount(ShardId, SourceAccountId);

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var depositContext = new CloudDbContext(options))
        {
            var depositOutcome = await new CloudCustodyBoundary(depositContext).DepositAsync(biotaId, ShardId, sourceOwnerId, Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);
        }

        await using var holdConnection = new MySqlConnection(_fixture.CloudConnectionString);
        await holdConnection.OpenAsync();
        await using var holdTransaction = await holdConnection.BeginTransactionAsync();

        // Holds the source's Cloud Custody Record row locked, uncommitted -- standing in for a
        // Withdrawal Reservation attempt that is mid-flight for the exact same item LinkAsync is
        // about to reassign.
        await LockCustodyRecordRowAsync(holdConnection, holdTransaction, biotaId);

        var linkTask = Task.Run(async () =>
        {
            await using var linkContext = new CloudDbContext(options);
            return await new CloudAccountLinkGateway(linkContext).LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());
        });

        var completedEarly = await Task.WhenAny(linkTask, Task.Delay(TimeSpan.FromSeconds(2))) == linkTask;
        Assert.IsFalse(
            completedEarly,
            "LinkAsync must serialize against a concurrent Withdrawal Reservation attempt for the source's Cloud Custody Record instead of racing past it.");

        // The reservation now commits, strictly between LinkAsync's own obligations check and its
        // reassignment of this exact row.
        var tokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        await InsertWithdrawalReservationAsync(holdConnection, holdTransaction, biotaId, sourceOwnerId, tokenHash);
        await holdTransaction.CommitAsync();

        var outcome = await linkTask;

        Assert.IsFalse(outcome.IsApproved, "A Withdrawal Reservation that commits mid-link must block the link instead of being silently orphaned by the reassignment.");
        Assert.AreEqual(CloudAccountLinkRejectionCode.SourceHasPendingObligations, outcome.RejectionCode);

        await using var verifyContext = new CloudDbContext(options);
        var record = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(sourceOwnerId, record.OwnerId, "A rejected link must never reassign the source's Cloud Custody Record.");

        var reservation = await verifyContext.CloudWithdrawalReservations.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(CloudReservationStatus.Active, reservation.Status);
        Assert.AreEqual(sourceOwnerId, reservation.OwnerId);
    }

    [TestMethod]
    public async Task LinkAsync_WouldCreateAnActiveAuctionConflict_IsRejected()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);

        var outcome = await new CloudAccountLinkGateway(context)
            .LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid(), wouldCreateActiveAuctionConflict: true);

        Assert.IsFalse(outcome.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.WouldCreateAuctionConflict, outcome.RejectionCode);
    }

    [TestMethod]
    public async Task LinkAsync_ConcurrentLinkAttemptsForTheSameSourceIntoDifferentMains_OnlyOneSucceeds()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        const uint firstMainAccountId = 700;
        const uint secondMainAccountId = 701;

        await using var firstContext = new CloudDbContext(options);
        await using var secondContext = new CloudDbContext(options);

        var firstTask = new CloudAccountLinkGateway(firstContext).LinkAsync(ShardId, firstMainAccountId, SourceAccountId, Guid.NewGuid());
        var secondTask = new CloudAccountLinkGateway(secondContext).LinkAsync(ShardId, secondMainAccountId, SourceAccountId, Guid.NewGuid());

        var outcomes = await Task.WhenAll(firstTask, secondTask);

        Assert.AreEqual(1, outcomes.Count(o => o.IsApproved), "Exactly one concurrent link attempt for the same source account must win.");
        Assert.AreEqual(1, outcomes.Count(o => !o.IsApproved && o.RejectionCode == CloudAccountLinkRejectionCode.SourceAlreadyLinked));

        await using var verifyContext = new CloudDbContext(options);
        var markerCount = await verifyContext.CloudActiveAccountLinkMarkers.CountAsync(m => m.AccountId == SourceAccountId);
        Assert.AreEqual(1, markerCount);
    }

    [TestMethod]
    public async Task UnlinkAsync_AnActiveLink_EndsItAndRoutesFutureDepositsBackToTheSourceAccount_WithoutRestoringPriorOwnership()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var sourceOwnerId = CloudOwnerIdentity.ForAccount(ShardId, SourceAccountId);
        var mainOwnerId = CloudOwnerIdentity.ForAccount(ShardId, MainAccountId);

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var depositContext = new CloudDbContext(options))
        {
            var depositOutcome = await new CloudCustodyBoundary(depositContext).DepositAsync(biotaId, ShardId, sourceOwnerId, Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);
        }

        await using (var linkContext = new CloudDbContext(options))
        {
            var linkOutcome = await new CloudAccountLinkGateway(linkContext).LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());
            Assert.IsTrue(linkOutcome.IsApproved);
        }

        await using (var resolveBeforeContext = new CloudDbContext(options))
        {
            var effectiveBefore = await new CloudAccountLinkGateway(resolveBeforeContext).ResolveEffectiveOwnerAccountIdAsync(ShardId, SourceAccountId);
            Assert.AreEqual(MainAccountId, effectiveBefore, "While linked, deposits must route to the Main Account.");
        }

        await using (var unlinkContext = new CloudDbContext(options))
        {
            var unlinkOutcome = await new CloudAccountLinkGateway(unlinkContext).UnlinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());
            Assert.IsTrue(unlinkOutcome.IsApproved);
        }

        await using (var resolveAfterContext = new CloudDbContext(options))
        {
            var effectiveAfter = await new CloudAccountLinkGateway(resolveAfterContext).ResolveEffectiveOwnerAccountIdAsync(ShardId, SourceAccountId);
            Assert.AreEqual(SourceAccountId, effectiveAfter, "After unlinking, future deposits must route to the newly independent account.");
        }

        await using var verifyContext = new CloudDbContext(options);
        var record = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(mainOwnerId, record.OwnerId, "Unlinking must never restore assets already transferred to the Main Account.");
    }

    [TestMethod]
    public async Task UnlinkAsync_NoActiveLinkExists_IsRejectedAsNotActive()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);

        var outcome = await new CloudAccountLinkGateway(context).UnlinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());

        Assert.IsFalse(outcome.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.LinkNotActive, outcome.RejectionCode);
    }

    [TestMethod]
    public async Task UnlinkAsync_RepeatedWithTheSameIdempotencyKey_ReplaysTheOriginalResult()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var linkContext = new CloudDbContext(options))
        {
            var linkOutcome = await new CloudAccountLinkGateway(linkContext).LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());
            Assert.IsTrue(linkOutcome.IsApproved);
        }

        var idempotencyKey = Guid.NewGuid();

        await using var firstContext = new CloudDbContext(options);
        var firstOutcome = await new CloudAccountLinkGateway(firstContext).UnlinkAsync(ShardId, MainAccountId, SourceAccountId, idempotencyKey);
        Assert.IsTrue(firstOutcome.IsApproved);

        await using var secondContext = new CloudDbContext(options);
        var secondOutcome = await new CloudAccountLinkGateway(secondContext).UnlinkAsync(ShardId, MainAccountId, SourceAccountId, idempotencyKey);

        Assert.IsTrue(secondOutcome.IsApproved);
        Assert.AreEqual(firstOutcome.AccountLinkId, secondOutcome.AccountLinkId);
    }

    [TestMethod]
    public async Task ResolveEffectiveOwnerAccountIdAsync_NeverLinked_ReturnsTheAccountItself()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);

        var effective = await new CloudAccountLinkGateway(context).ResolveEffectiveOwnerAccountIdAsync(ShardId, SourceAccountId);

        Assert.AreEqual(SourceAccountId, effective);
    }

    [TestMethod]
    public async Task GetOwnershipGroupAccountIdsAsync_NeverLinked_ReturnsOnlyTheAccountItself()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);

        var groupAccountIds = await new CloudAccountLinkGateway(context).GetOwnershipGroupAccountIdsAsync(ShardId, SourceAccountId);

        CollectionAssert.AreEquivalent(new[] { SourceAccountId }, groupAccountIds.ToArray());
    }

    [TestMethod]
    public async Task GetOwnershipGroupAccountIdsAsync_QueriedFromEitherTheMainOrTheLinkedAccount_ReturnsTheWholeGroup()
    {
        // AC Cloud Mule review of PR #120, finding [P1]: Player_CloudWithdrawal.RedeemAsync used to
        // compare a reservation's owner identity directly against the redeeming account's own raw
        // identity, so a Withdrawal Token opened under one side of a link became unredeemable by a
        // character on the other side. Both directions of this group query are what closes that gap.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var linkContext = new CloudDbContext(options))
        {
            var linkOutcome = await new CloudAccountLinkGateway(linkContext).LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());
            Assert.IsTrue(linkOutcome.IsApproved);
        }

        await using var queryFromMainContext = new CloudDbContext(options);
        var groupFromMain = await new CloudAccountLinkGateway(queryFromMainContext).GetOwnershipGroupAccountIdsAsync(ShardId, MainAccountId);
        CollectionAssert.AreEquivalent(new[] { MainAccountId, SourceAccountId }, groupFromMain.ToArray());

        await using var queryFromLinkedContext = new CloudDbContext(options);
        var groupFromLinked = await new CloudAccountLinkGateway(queryFromLinkedContext).GetOwnershipGroupAccountIdsAsync(ShardId, SourceAccountId);
        CollectionAssert.AreEquivalent(new[] { MainAccountId, SourceAccountId }, groupFromLinked.ToArray());
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    private static async Task LockCustodyRecordRowAsync(MySqlConnection connection, MySqlTransaction transaction, uint biotaId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM CloudCustodyRecord WHERE BiotaId = @biotaId FOR UPDATE;";
        command.Parameters.AddWithValue("@biotaId", biotaId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
    }

    private static async Task InsertWithdrawalReservationAsync(
        MySqlConnection connection, MySqlTransaction transaction, uint biotaId, Guid ownerId, string tokenHash)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CloudWithdrawalReservation
                (Id, ShardId, BiotaId, OwnerId, TokenHash, OpenIdempotencyKey, Status, Version, ExpiresAtUtc)
            VALUES
                (@id, @shardId, @biotaId, @ownerId, @tokenHash, @openIdempotencyKey, 'Active', 1, @expiresAtUtc);
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@shardId", ShardId);
        command.Parameters.AddWithValue("@biotaId", biotaId);
        command.Parameters.AddWithValue("@ownerId", ownerId.ToString());
        command.Parameters.AddWithValue("@tokenHash", tokenHash);
        command.Parameters.AddWithValue("@openIdempotencyKey", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@expiresAtUtc", DateTime.UtcNow.AddMinutes(15));
        await command.ExecuteNonQueryAsync();
    }
}
