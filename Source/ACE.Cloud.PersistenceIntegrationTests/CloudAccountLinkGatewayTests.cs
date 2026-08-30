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
    private const uint AdminAccessLevel = 5;
    private const uint AdminAccountId = 999;

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
                [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)],
                ShardId, sourceOwnerId, Convert.ToHexString(Guid.NewGuid().ToByteArray()), TimeSpan.FromMinutes(15), Guid.NewGuid());
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

        var target = await verifyContext.CloudWithdrawalReservationTargets.AsNoTracking().SingleAsync(t => t.ItemBiotaId == biotaId);
        var reservation = await verifyContext.CloudWithdrawalReservations.AsNoTracking().SingleAsync(r => r.Id == target.ReservationId);
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
    public async Task LinkAsync_ANewGroupInsertForTheSameMainCommitsMidTransaction_TheLinkIsApprovedByReusingItInsteadOfMisreportingSourceAlreadyLinked()
    {
        // AC Cloud Mule review of PR #120, finding [P1]: LinkAsync used to resolve a not-yet-existing
        // Main Account's CloudOwnershipGroup with a plain, unlocked read. A concurrent LinkAsync call
        // for the same brand-new Main but a *different*, genuinely standalone source account could
        // insert-and-commit that CloudOwnershipGroup row strictly between this unlocked read and this
        // transaction's own insert; the resulting UQ_CloudOwnershipGroup_Shard_Main collision was then
        // misreported as SourceAlreadyLinked -- a code the enum defines as "the source account is
        // already a Linked Account of some Main Account," which was never true for this source.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        const uint newMainAccountId = 900;
        const uint sourceAccountId = 902;

        await using var holdConnection = new MySqlConnection(_fixture.CloudConnectionString);
        await holdConnection.OpenAsync();
        await using var holdTransaction = await holdConnection.BeginTransactionAsync();

        // Stands in for a concurrent LinkAsync(newMainAccountId, otherSourceAccountId=901) that has
        // already inserted (but not yet committed) newMainAccountId's brand-new CloudOwnershipGroup.
        var otherGroupId = await InsertOwnershipGroupRowAsync(holdConnection, holdTransaction, newMainAccountId);

        var linkTask = Task.Run(async () =>
        {
            await using var linkContext = new CloudDbContext(options);
            return await new CloudAccountLinkGateway(linkContext).LinkAsync(ShardId, newMainAccountId, sourceAccountId, Guid.NewGuid());
        });

        var completedEarly = await Task.WhenAny(linkTask, Task.Delay(TimeSpan.FromSeconds(2))) == linkTask;
        Assert.IsFalse(
            completedEarly,
            "LinkAsync's own-Main group resolution must serialize against a concurrent insert of the same not-yet-committed group row instead of reading an unlocked, invisible-until-commit row.");

        await holdTransaction.CommitAsync();

        var outcome = await linkTask;
        Assert.IsTrue(
            outcome.IsApproved,
            $"Linking a brand-new Main from a standalone source must never be misreported as a conflict; got {outcome.RejectionCode}.");

        await using var verifyContext = new CloudDbContext(options);
        var groupCount = await verifyContext.CloudOwnershipGroups.CountAsync(g => g.ShardId == ShardId && g.MainAccountId == newMainAccountId);
        Assert.AreEqual(1, groupCount, "The link must reuse the concurrently-committed group instead of creating a second one.");

        var reusedGroupId = await verifyContext.CloudActiveAccountLinkMarkers.AsNoTracking()
            .Where(m => m.ShardId == ShardId && m.AccountId == sourceAccountId)
            .Select(m => m.OwnershipGroupId)
            .SingleAsync();
        Assert.AreEqual(otherGroupId, reusedGroupId);
    }

    [TestMethod]
    public async Task LinkAsync_ANewChildForTheSourceAccountCommitsMidTransaction_TheLinkIsRejectedInsteadOfFormingAForbiddenTree()
    {
        // AC Cloud Mule review of PR #120, finding [P1]: SourceHasActiveChildrenAsync used to be a
        // plain, unlocked read of the source account's own CloudOwnershipGroup row -- the only
        // eligibility input in LinkAsync without a locked read, unlike mainMarker/sourceMarker and the
        // pending-obligations check. A concurrent LinkAsync call that makes the source account itself
        // a Main with a new child could insert-and-commit that group/marker row strictly between this
        // unlocked read and this transaction's own commit, letting the forbidden 3-level tree
        // (Main -> Source -> NewChild) form. The fix locks that same row before deciding, so a
        // concurrent insert of it must serialize against this check instead of racing past it.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        const uint mainAccountId = 950;
        const uint sourceAccountId = 951;
        const uint newChildAccountId = 952;

        await using var holdConnection = new MySqlConnection(_fixture.CloudConnectionString);
        await holdConnection.OpenAsync();
        await using var holdTransaction = await holdConnection.BeginTransactionAsync();

        // Stands in for a concurrent LinkAsync(mainAccountId: sourceAccountId, sourceAccountId:
        // newChildAccountId) that has already inserted (but not yet committed) sourceAccountId's own
        // CloudOwnershipGroup and the active marker for its new child.
        await InsertActiveChildLinkAsync(holdConnection, holdTransaction, sourceAccountId, newChildAccountId);

        var linkTask = Task.Run(async () =>
        {
            await using var linkContext = new CloudDbContext(options);
            return await new CloudAccountLinkGateway(linkContext).LinkAsync(ShardId, mainAccountId, sourceAccountId, Guid.NewGuid());
        });

        var completedEarly = await Task.WhenAny(linkTask, Task.Delay(TimeSpan.FromSeconds(2))) == linkTask;
        Assert.IsFalse(
            completedEarly,
            "LinkAsync's source-has-active-children check must serialize against a concurrent, not-yet-committed insert of the source's own group row instead of reading an unlocked, invisible-until-commit row.");

        await holdTransaction.CommitAsync();

        var outcome = await linkTask;
        Assert.IsFalse(outcome.IsApproved, "A source that concurrently gained an active child must never also be linked elsewhere.");
        Assert.AreEqual(CloudAccountLinkRejectionCode.SourceHasLinkedAccounts, outcome.RejectionCode);
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

    /// <summary>
    /// Red -&gt; Green regression test for issue #23's review [P1]: this PR switched
    /// <see cref="CloudAccountLinkGateway"/> from a hardcoded <see cref="CloudMutationGateState.Open"/>
    /// to the real resolved gate, but no test proved Global Cloud Maintenance actually blocks
    /// <see cref="CloudAccountLinkGateway.LinkAsync"/>, unlike every <c>CloudCustodyBoundary</c> call
    /// site (each of which has its own <c>WhileFrozen_*</c> test).
    /// </summary>
    [TestMethod]
    public async Task WhileFrozen_LinkAsync_IsRejected_ProvingTheRealGateBlocksTheLinkCallSite()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var maintenanceContext = new CloudDbContext(options))
        {
            var maintenanceBoundary = new CloudGlobalMaintenanceBoundary(maintenanceContext);
            var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
            Assert.AreEqual(
                CloudBoundaryOutcomeKind.Committed,
                (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);
        }

        await using var context = new CloudDbContext(options);
        var outcome = await new CloudAccountLinkGateway(context).LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());

        Assert.IsFalse(outcome.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.MutationsFrozen, outcome.RejectionCode);
    }

    /// <summary>
    /// Red -&gt; Green regression test for issue #23's review [P1]: the same gap as
    /// <see cref="WhileFrozen_LinkAsync_IsRejected_ProvingTheRealGateBlocksTheLinkCallSite"/>, for
    /// <see cref="CloudAccountLinkGateway.UnlinkAsync"/>.
    /// </summary>
    [TestMethod]
    public async Task WhileFrozen_UnlinkAsync_IsRejected_ProvingTheRealGateBlocksTheUnlinkCallSite()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var linkContext = new CloudDbContext(options))
        {
            var linkOutcome = await new CloudAccountLinkGateway(linkContext).LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());
            Assert.IsTrue(linkOutcome.IsApproved);
        }

        await using (var maintenanceContext = new CloudDbContext(options))
        {
            var maintenanceBoundary = new CloudGlobalMaintenanceBoundary(maintenanceContext);
            var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
            Assert.AreEqual(
                CloudBoundaryOutcomeKind.Committed,
                (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);
        }

        await using var context = new CloudDbContext(options);
        var outcome = await new CloudAccountLinkGateway(context).UnlinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());

        Assert.IsFalse(outcome.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.MutationsFrozen, outcome.RejectionCode);
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

    [TestMethod]
    public async Task TryGetOwnershipGroupIdAsync_NeverLinked_ReturnsNull()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);

        var groupId = await new CloudAccountLinkGateway(context).TryGetOwnershipGroupIdAsync(ShardId, MainAccountId);

        Assert.IsNull(groupId);
    }

    [TestMethod]
    public async Task TryGetOwnershipGroupIdAsync_AfterALink_ReturnsTheSameGroupIdTheLinkApproved()
    {
        // Issue #33: the account overview endpoint and Display Character reselection after a
        // link/unlink both need this ID to reach CloudDisplayCharacterGateway.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        Guid approvedGroupId;
        await using (var linkContext = new CloudDbContext(options))
        {
            var linkOutcome = await new CloudAccountLinkGateway(linkContext).LinkAsync(ShardId, MainAccountId, SourceAccountId, Guid.NewGuid());
            Assert.IsTrue(linkOutcome.IsApproved);
            approvedGroupId = linkOutcome.OwnershipGroupId!.Value;
        }

        await using var queryContext = new CloudDbContext(options);
        var groupId = await new CloudAccountLinkGateway(queryContext).TryGetOwnershipGroupIdAsync(ShardId, MainAccountId);

        Assert.AreEqual(approvedGroupId, groupId);
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

    private static async Task<Guid> InsertOwnershipGroupRowAsync(MySqlConnection connection, MySqlTransaction transaction, uint mainAccountId)
    {
        var groupId = Guid.NewGuid();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CloudOwnershipGroup (Id, ShardId, MainAccountId, CreatedAtUtc)
            VALUES (@id, @shardId, @mainAccountId, UTC_TIMESTAMP(6));
            """;
        command.Parameters.AddWithValue("@id", groupId.ToString());
        command.Parameters.AddWithValue("@shardId", ShardId);
        command.Parameters.AddWithValue("@mainAccountId", mainAccountId);
        await command.ExecuteNonQueryAsync();
        return groupId;
    }

    /// <summary>
    /// Raw-inserts the same three rows LinkAsync itself would insert to make
    /// <paramref name="mainAccountId"/> a Main Account with <paramref name="childAccountId"/> as an
    /// active child, without going through the gateway's own transaction -- so the caller can hold
    /// them open, uncommitted, to simulate a concurrent link landing mid-transaction.
    /// </summary>
    private static async Task InsertActiveChildLinkAsync(
        MySqlConnection connection, MySqlTransaction transaction, uint mainAccountId, uint childAccountId)
    {
        var groupId = await InsertOwnershipGroupRowAsync(connection, transaction, mainAccountId);
        var linkId = Guid.NewGuid();

        await using (var linkCommand = connection.CreateCommand())
        {
            linkCommand.Transaction = transaction;
            linkCommand.CommandText = """
                INSERT INTO CloudAccountLink (Id, OwnershipGroupId, ShardId, LinkedAccountId, Status, LinkedAtUtc, UnlinkedAtUtc)
                VALUES (@id, @groupId, @shardId, @childAccountId, 'Active', UTC_TIMESTAMP(6), NULL);
                """;
            linkCommand.Parameters.AddWithValue("@id", linkId.ToString());
            linkCommand.Parameters.AddWithValue("@groupId", groupId.ToString());
            linkCommand.Parameters.AddWithValue("@shardId", ShardId);
            linkCommand.Parameters.AddWithValue("@childAccountId", childAccountId);
            await linkCommand.ExecuteNonQueryAsync();
        }

        await using (var markerCommand = connection.CreateCommand())
        {
            markerCommand.Transaction = transaction;
            markerCommand.CommandText = """
                INSERT INTO CloudActiveAccountLinkMarker (ShardId, AccountId, AccountLinkId, OwnershipGroupId, CreatedAtUtc)
                VALUES (@shardId, @childAccountId, @linkId, @groupId, UTC_TIMESTAMP(6));
                """;
            markerCommand.Parameters.AddWithValue("@shardId", ShardId);
            markerCommand.Parameters.AddWithValue("@childAccountId", childAccountId);
            markerCommand.Parameters.AddWithValue("@linkId", linkId.ToString());
            markerCommand.Parameters.AddWithValue("@groupId", groupId.ToString());
            await markerCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertWithdrawalReservationAsync(
        MySqlConnection connection, MySqlTransaction transaction, uint biotaId, Guid ownerId, string tokenHash)
    {
        var reservationId = Guid.NewGuid();

        await using (var reservationCommand = connection.CreateCommand())
        {
            reservationCommand.Transaction = transaction;
            reservationCommand.CommandText = """
                INSERT INTO CloudWithdrawalReservation
                    (Id, ShardId, OwnerId, TokenHash, OpenIdempotencyKey, Status, Version, ExpiresAtUtc)
                VALUES
                    (@id, @shardId, @ownerId, @tokenHash, @openIdempotencyKey, 'Active', 1, @expiresAtUtc);
                """;
            reservationCommand.Parameters.AddWithValue("@id", reservationId.ToString());
            reservationCommand.Parameters.AddWithValue("@shardId", ShardId);
            reservationCommand.Parameters.AddWithValue("@ownerId", ownerId.ToString());
            reservationCommand.Parameters.AddWithValue("@tokenHash", tokenHash);
            reservationCommand.Parameters.AddWithValue("@openIdempotencyKey", Guid.NewGuid().ToString());
            reservationCommand.Parameters.AddWithValue("@expiresAtUtc", DateTime.UtcNow.AddMinutes(15));
            await reservationCommand.ExecuteNonQueryAsync();
        }

        await using (var targetCommand = connection.CreateCommand())
        {
            targetCommand.Transaction = transaction;
            targetCommand.CommandText = """
                INSERT INTO CloudWithdrawalReservationTarget (Id, ReservationId, Kind, ItemBiotaId)
                VALUES (@id, @reservationId, 'Item', @biotaId);
                """;
            targetCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            targetCommand.Parameters.AddWithValue("@reservationId", reservationId.ToString());
            targetCommand.Parameters.AddWithValue("@biotaId", biotaId);
            await targetCommand.ExecuteNonQueryAsync();
        }
    }
}
