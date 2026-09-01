using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #35's Transfer Offer gateway (XFER-001, XFER-002, INV-002, INV-004..006,
/// EVT-001, EVT-003), against the real <see cref="CloudDatabaseFixture"/> MariaDB instance rather than
/// the pure <c>CloudTransferOfferPolicy</c> specification (<c>ACE.Cloud.Domain.Tests</c>) alone --
/// <see cref="CloudTransferOfferGateway"/>'s own doc comment explains why the policy is not literally
/// called at runtime, so only a database-backed test exercises the locked multi-target creation,
/// cross-reservation exclusivity, idempotent create/accept/decline/cancel/expire, and ledger/outbox/
/// notification atomicity this class actually performs (.claude-review.md P0 finding on PR #153).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudTransferOfferGatewayTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 600_000;

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
    public async Task Accept_AMixedTwoItemOffer_TransfersEveryTargetAtomically_AndCommitsLedgerOutboxAndNotificationTogether()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var firstBiotaId = NextId();
        var secondBiotaId = NextId();
        const string recipientCharacterName = "Recipient1";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);
        var recipientOwnerId = CloudOwnerIdentity.ForAccount(ShardId, recipientAccountId);

        await using var context = new CloudDbContext(options);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(firstBiotaId, ShardId, senderOwnerId, Guid.NewGuid());
        await custodyBoundary.DepositAsync(secondBiotaId, ShardId, senderOwnerId, Guid.NewGuid());

        var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));

        var createOutcome = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName,
            [CloudTransferOfferRequestTarget.ForItem(firstBiotaId), CloudTransferOfferRequestTarget.ForItem(secondBiotaId)],
            Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, createOutcome.Kind, createOutcome.Reason);
        var offerId = createOutcome.Value!.Id;

        var acceptOutcome = await gateway.AcceptAsync(offerId, recipientAccountId, createOutcome.Value!.Version);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, acceptOutcome.Kind, acceptOutcome.Reason);
        Assert.AreEqual(CloudTransferOfferStatus.Accepted, acceptOutcome.Value!.Status);

        await using var verifyContext = new CloudDbContext(options);
        var firstRecord = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == firstBiotaId);
        var secondRecord = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == secondBiotaId);
        Assert.AreEqual(recipientOwnerId, firstRecord.OwnerId, "Accept must transfer ownership of every offered target.");
        Assert.AreEqual(recipientOwnerId, secondRecord.OwnerId);

        var ledgerEvents = await verifyContext.CloudActivityLedgerEvents
            .Where(e => e.BiotaId == firstBiotaId || e.BiotaId == secondBiotaId)
            .ToListAsync();
        Assert.AreEqual(2, ledgerEvents.Count(e => e.EventType == CloudBoundaryOperationType.TransferOfferCreated));
        Assert.AreEqual(2, ledgerEvents.Count(e => e.EventType == CloudBoundaryOperationType.OwnershipTransfer));

        var outboxEvents = await verifyContext.CloudCustodyOutboxEvents
            .Where(e => e.BiotaId == firstBiotaId || e.BiotaId == secondBiotaId)
            .ToListAsync();
        Assert.AreEqual(
            2, outboxEvents.Count(e => e.EventType == CloudBoundaryOperationType.OwnershipTransfer),
            "Accept must append one OwnershipTransfer outbox event per transferred target, in the same transaction as the offer's own resolution.");

        Assert.IsTrue(await verifyContext.CloudNotifications.AnyAsync(
            n => n.ShardId == ShardId && n.OwnerId == recipientOwnerId && n.Kind == CloudNotificationKind.TransferOfferReceived));
        Assert.IsTrue(await verifyContext.CloudNotifications.AnyAsync(
            n => n.ShardId == ShardId && n.OwnerId == senderOwnerId && n.Kind == CloudNotificationKind.TransferOfferAccepted));
    }

    [TestMethod]
    public async Task CreateAsync_RepeatedIdempotencyKey_ReplaysTheOriginalOffer_WithoutOpeningASecondOne()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var biotaId = NextId();
        const string recipientCharacterName = "Recipient2";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);
        var idempotencyKey = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        await new CloudCustodyBoundary(context).DepositAsync(biotaId, ShardId, senderOwnerId, Guid.NewGuid());

        var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));

        var first = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], idempotencyKey);
        var second = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind, second.Reason);
        Assert.AreEqual(first.Value!.Id, second.Value!.Id);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.Set<CloudTransferOfferRecord>().CountAsync(o => o.CreateIdempotencyKey == idempotencyKey));
        Assert.AreEqual(1, await verifyContext.Set<CloudTransferOfferTargetRecord>().CountAsync(t => t.ItemBiotaId == biotaId));
    }

    [TestMethod]
    public async Task AcceptAsync_RepeatedAfterAlreadyAccepted_IsIdempotent_AndDoesNotReapplyTheTransfer()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var biotaId = NextId();
        const string recipientCharacterName = "Recipient3";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);

        await using var context = new CloudDbContext(options);
        await new CloudCustodyBoundary(context).DepositAsync(biotaId, ShardId, senderOwnerId, Guid.NewGuid());

        var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));
        var createOutcome = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());
        var offerId = createOutcome.Value!.Id;

        var first = await gateway.AcceptAsync(offerId, recipientAccountId, createOutcome.Value!.Version);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind, first.Reason);

        var second = await gateway.AcceptAsync(offerId, recipientAccountId, createOutcome.Value!.Version);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind, "Repeating an already-applied terminal command is a no-op success.");

        await using var verifyContext = new CloudDbContext(options);
        var transferOutboxEvents = await verifyContext.CloudCustodyOutboxEvents
            .Where(e => e.BiotaId == biotaId && e.EventType == CloudBoundaryOperationType.OwnershipTransfer)
            .ToListAsync();
        Assert.AreEqual(1, transferOutboxEvents.Count, "Replaying an already-Accepted command must not reapply the ownership transfer a second time.");
    }

    [TestMethod]
    public async Task CreateAsync_ForATargetAlreadyHeldByAnActiveWithdrawalReservation_ReturnsConflict()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var biotaId = NextId();
        const string recipientCharacterName = "Recipient4";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);

        await using var context = new CloudDbContext(options);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(biotaId, ShardId, senderOwnerId, Guid.NewGuid());

        var reserveOutcome = await custodyBoundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, senderOwnerId,
            Convert.ToHexString(Guid.NewGuid().ToByteArray()), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));
        var createOutcome = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());

        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Conflict, createOutcome.Kind,
            "A target already exclusively held by an active Withdrawal Reservation must refuse a new Transfer Offer.");
    }

    [TestMethod]
    public async Task ReserveForWithdrawal_ForATargetAlreadyHeldByAPendingTransferOffer_ReturnsConflict()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var biotaId = NextId();
        const string recipientCharacterName = "Recipient5";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);

        await using var context = new CloudDbContext(options);
        await new CloudCustodyBoundary(context).DepositAsync(biotaId, ShardId, senderOwnerId, Guid.NewGuid());

        var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));
        var createOutcome = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, createOutcome.Kind, createOutcome.Reason);

        var custodyBoundary = new CloudCustodyBoundary(context);
        var reserveOutcome = await custodyBoundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, senderOwnerId,
            Convert.ToHexString(Guid.NewGuid().ToByteArray()), TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Conflict, reserveOutcome.Kind,
            "A target already exclusively held by a Pending Transfer Offer must refuse a new Withdrawal Reservation.");
    }

    [TestMethod]
    public async Task AcceptAsync_WithAStaleExpectedVersion_ReturnsConflict()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var biotaId = NextId();
        const string recipientCharacterName = "Recipient6";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);

        await using var context = new CloudDbContext(options);
        await new CloudCustodyBoundary(context).DepositAsync(biotaId, ShardId, senderOwnerId, Guid.NewGuid());

        var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));
        var createOutcome = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());
        var offerId = createOutcome.Value!.Id;

        var acceptOutcome = await gateway.AcceptAsync(offerId, recipientAccountId, expectedVersion: createOutcome.Value!.Version + 1);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, acceptOutcome.Kind);
        StringAssert.Contains(acceptOutcome.Reason, "version");
    }

    [TestMethod]
    public async Task AcceptAndDecline_RacingConcurrently_ExactlyOneCommandWins()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var biotaId = NextId();
        const string recipientCharacterName = "Recipient7";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);

        Guid offerId;
        int version;
        await using (var setupContext = new CloudDbContext(options))
        {
            await new CloudCustodyBoundary(setupContext).DepositAsync(biotaId, ShardId, senderOwnerId, Guid.NewGuid());
            var setupGateway = new CloudTransferOfferGateway(setupContext, new CloudAccountLinkGateway(setupContext));
            var createOutcome = await setupGateway.CreateAsync(
                ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, createOutcome.Kind, createOutcome.Reason);
            offerId = createOutcome.Value!.Id;
            version = createOutcome.Value!.Version;
        }

        await using var acceptContext = new CloudDbContext(options);
        await using var declineContext = new CloudDbContext(options);
        var acceptGateway = new CloudTransferOfferGateway(acceptContext, new CloudAccountLinkGateway(acceptContext));
        var declineGateway = new CloudTransferOfferGateway(declineContext, new CloudAccountLinkGateway(declineContext));

        var acceptTask = acceptGateway.AcceptAsync(offerId, recipientAccountId, version);
        var declineTask = declineGateway.DeclineAsync(offerId, recipientAccountId, version);
        await Task.WhenAll(acceptTask, declineTask);

        var results = new[] { acceptTask.Result.Kind, declineTask.Result.Kind };
        Assert.AreEqual(1, results.Count(k => k == CloudBoundaryOutcomeKind.Committed), "Exactly one terminal command must win the race.");
        Assert.AreEqual(1, results.Count(k => k == CloudBoundaryOutcomeKind.Conflict), "The loser must observe the offer is no longer Pending, not silently succeed too.");

        await using var verifyContext = new CloudDbContext(options);
        var finalOffer = await verifyContext.Set<CloudTransferOfferRecord>().AsNoTracking().SingleAsync(o => o.Id == offerId);
        Assert.IsTrue(finalOffer.Status is CloudTransferOfferStatus.Accepted or CloudTransferOfferStatus.Declined);
        var expectedWinnerStatus = acceptTask.Result.Kind == CloudBoundaryOutcomeKind.Committed
            ? CloudTransferOfferStatus.Accepted
            : CloudTransferOfferStatus.Declined;
        Assert.AreEqual(expectedWinnerStatus, finalOffer.Status, "Exactly one terminal state must win, matching whichever command actually committed.");
    }

    [TestMethod]
    public async Task ExpireDueOffersAsync_SweepsAnOverdueOffer_ReleasesItsTarget_AndAppendsLedgerAndNotification()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var biotaId = NextId();
        const string recipientCharacterName = "Recipient8";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);

        await using var context = new CloudDbContext(options);
        await new CloudCustodyBoundary(context).DepositAsync(biotaId, ShardId, senderOwnerId, Guid.NewGuid());

        var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));
        var createOutcome = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());
        var offerId = createOutcome.Value!.Id;

        // XFER-002's real "seven days" duration is fixed, not caller-configurable; back-date the
        // persisted deadline so the sweep below finds a genuinely overdue offer deterministically,
        // without a test sleep, mirroring CloudGlobalMaintenanceBoundaryTests' own back-dating pattern.
        await BackdateOfferExpiryAsync(offerId, TimeSpan.FromDays(8));

        var expiredCount = await gateway.ExpireDueOffersAsync(ShardId);
        Assert.AreEqual(1, expiredCount);

        await using var verifyContext = new CloudDbContext(options);
        var offer = await verifyContext.Set<CloudTransferOfferRecord>().AsNoTracking().SingleAsync(o => o.Id == offerId);
        Assert.AreEqual(CloudTransferOfferStatus.Expired, offer.Status);

        Assert.IsTrue(await verifyContext.CloudActivityLedgerEvents.AnyAsync(
            e => e.BiotaId == biotaId && e.EventType == CloudBoundaryOperationType.TransferOfferExpired));
        Assert.IsTrue(await verifyContext.CloudNotifications.AnyAsync(
            n => n.ShardId == ShardId && n.OwnerId == senderOwnerId && n.Kind == CloudNotificationKind.TransferOfferExpired));

        var reopenOutcome = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reopenOutcome.Kind, "Expiry must free the target for a fresh offer.");
    }

    [TestMethod]
    public async Task CancelAsync_ReleasesEveryTargetAtomically_FreeingThemForANewOffer()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var itemBiotaId = NextId();
        var stackBiotaId = NextId();
        const string recipientCharacterName = "Recipient9";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, itemBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, stackBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);

        await using var context = new CloudDbContext(options);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(itemBiotaId, ShardId, senderOwnerId, Guid.NewGuid());
        var stackDeposit = await custodyBoundary.DepositStackAsync(stackBiotaId, ShardId, senderOwnerId, 10, Guid.NewGuid());

        var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));
        var createOutcome = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName,
            [CloudTransferOfferRequestTarget.ForItem(itemBiotaId), CloudTransferOfferRequestTarget.ForStackLot(stackDeposit.Value!.Lot.Id)],
            Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, createOutcome.Kind, createOutcome.Reason);

        var cancelOutcome = await gateway.CancelAsync(createOutcome.Value!.Id, senderAccountId, createOutcome.Value!.Version);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, cancelOutcome.Kind, cancelOutcome.Reason);
        Assert.AreEqual(CloudTransferOfferStatus.Cancelled, cancelOutcome.Value!.Status);

        var reopenItem = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(itemBiotaId)], Guid.NewGuid());
        var reopenLot = await gateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForStackLot(stackDeposit.Value!.Lot.Id)], Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reopenItem.Kind, "Cancelling must free every target it held, including the item.");
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reopenLot.Kind, "Cancelling must free every target it held, including the Cloud Stack Lot.");
    }

    [TestMethod]
    public async Task Exit_ShiftsAPendingTransferOffersExpiry_ByExactlyTheLedgeredFrozenDuration_NeverReleasingIt()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var biotaId = NextId();
        const string recipientCharacterName = "Recipient10";
        const uint adminAccessLevel = 5;
        const uint adminAccountId = 999;

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);

        await using var offerContext = new CloudDbContext(options);
        await new CloudCustodyBoundary(offerContext).DepositAsync(biotaId, ShardId, senderOwnerId, Guid.NewGuid());
        var offerGateway = new CloudTransferOfferGateway(offerContext, new CloudAccountLinkGateway(offerContext));
        var createOutcome = await offerGateway.CreateAsync(
            ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, createOutcome.Kind, createOutcome.Reason);
        var offerId = createOutcome.Value!.Id;
        var expiresBeforeFreeze = createOutcome.Value!.ExpiresAtUtc;

        await using var maintenanceContext = new CloudDbContext(options);
        var maintenanceBoundary = new CloudGlobalMaintenanceBoundary(maintenanceContext);

        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        var entered = await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, adminAccessLevel, adminAccountId, initial.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, entered.Kind, entered.Reason);

        await BackdateMaintenanceEnteredAtUtcAsync(TimeSpan.FromMinutes(37));

        var exited = await maintenanceBoundary.ExitAsync(ShardId, confirmed: true, adminAccessLevel, entered.Value!.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, exited.Kind, exited.Reason);

        var exitEvent = await maintenanceContext.Set<CloudGlobalMaintenanceLedgerEvent>().AsNoTracking()
            .SingleAsync(e => e.EventType == CloudGlobalMaintenanceLedgerEventType.Exited);
        var frozenDuration = TimeSpan.FromSeconds(exitEvent.FrozenDurationSeconds!.Value);

        await using var verifyContext = new CloudDbContext(options);
        var offerAfterExit = await verifyContext.Set<CloudTransferOfferRecord>().AsNoTracking().SingleAsync(o => o.Id == offerId);

        Assert.AreEqual(
            expiresBeforeFreeze.Add(frozenDuration), offerAfterExit.ExpiresAtUtc,
            "ADM-004: resume must shift a Pending Transfer Offer's deadline by exactly the frozen duration.");
        Assert.AreEqual(
            CloudTransferOfferStatus.Pending, offerAfterExit.Status,
            "ADM-004: maintenance must never cancel or release an in-flight Transfer Offer.");
    }

    [TestMethod]
    public async Task Accept_AFiveItemOffer_LocksAndProcessesEveryTargetInCanonicalOrder_MatchingCreateAsyncsOwnDeterministicLockOrder()
    {
        // Regression coverage for the .claude-review.md P1 finding on PR #153: TryResolveOnceAsync used
        // to lock/process every target in whatever order the plain `Where(...).ToListAsync()` query
        // happened to return them, instead of CloudReservationTargetOrdering.Order's canonical order
        // TryCreateOnceAsync already uses -- a real cross-transaction deadlock risk (transaction rule
        // 2). CloudCustodyOutboxEvent.SequenceNumber is reserved once per target, strictly in the
        // Accept loop's own processing order, so it is a direct, deterministic witness of that order:
        // five targets make a coincidental ascending match on the old unordered code a ~1-in-120
        // chance, so this reliably fails before the fix and always passes after it.
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        const string recipientCharacterName = "Recipient11";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);

        var biotaIds = new List<uint>();
        for (var i = 0; i < 5; i++)
        {
            var biotaId = NextId();
            await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
            biotaIds.Add(biotaId);
        }

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);

        await using var context = new CloudDbContext(options);
        var custodyBoundary = new CloudCustodyBoundary(context);
        foreach (var biotaId in biotaIds)
        {
            await custodyBoundary.DepositAsync(biotaId, ShardId, senderOwnerId, Guid.NewGuid());
        }

        var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));

        // Request targets out of ascending order, so a passing assertion below cannot be explained by
        // the request's own ordering.
        var shuffledTargets = new[] { biotaIds[2], biotaIds[0], biotaIds[4], biotaIds[1], biotaIds[3] }
            .Select(CloudTransferOfferRequestTarget.ForItem).ToArray();

        var createOutcome = await gateway.CreateAsync(ShardId, senderAccountId, recipientCharacterName, shuffledTargets, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, createOutcome.Kind, createOutcome.Reason);

        var acceptOutcome = await gateway.AcceptAsync(createOutcome.Value!.Id, recipientAccountId, createOutcome.Value!.Version);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, acceptOutcome.Kind, acceptOutcome.Reason);

        await using var verifyContext = new CloudDbContext(options);
        var processingOrder = await verifyContext.CloudCustodyOutboxEvents
            .Where(e => biotaIds.Contains(e.BiotaId) && e.EventType == CloudBoundaryOperationType.OwnershipTransfer)
            .OrderBy(e => e.SequenceNumber)
            .Select(e => e.BiotaId)
            .ToListAsync();

        var expectedCanonicalOrder = biotaIds.OrderBy(id => id).ToList();
        CollectionAssert.AreEqual(
            expectedCanonicalOrder, processingOrder,
            "Every target must be locked/processed in CloudReservationTargetOrdering's canonical order, " +
            "the same order TryCreateOnceAsync uses, so a concurrent overlapping transaction can never deadlock against this one.");
    }

    private async Task BackdateOfferExpiryAsync(Guid offerId, TimeSpan by)
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CloudTransferOffer SET ExpiresAtUtc = DATE_SUB(ExpiresAtUtc, INTERVAL @seconds SECOND) WHERE Id = @id;";
        command.Parameters.AddWithValue("@seconds", by.TotalSeconds);
        command.Parameters.AddWithValue("@id", offerId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private async Task BackdateMaintenanceEnteredAtUtcAsync(TimeSpan by)
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CloudGlobalMaintenance SET EnteredAtUtc = DATE_SUB(EnteredAtUtc, INTERVAL @seconds SECOND) WHERE Id = 1;";
        command.Parameters.AddWithValue("@seconds", by.TotalSeconds);
        await command.ExecuteNonQueryAsync();
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
