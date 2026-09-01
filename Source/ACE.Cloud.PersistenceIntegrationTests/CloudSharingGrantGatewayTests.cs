using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// AC Cloud Mule issue #36's Red section against a real MariaDB (SHARE-001..004, AUTH-008, WDR-002):
/// current-character resolution, explicit set/revoke idempotence, guild(allegiance)-derived access,
/// conflicting grants, and grant-derived Withdrawal Token binding/invalidation on authority loss.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudSharingGrantGatewayTests
{
    private const string ShardId = "us1";
    private const uint OwnerAccountId = 500;
    private const uint GranteeAccountId = 600;
    private const uint ThirdPartyAccountId = 700;

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 900_000;

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
    public async Task SetAsync_ANewViewOnlyGrant_CreatesItAndNotifiesTheGrantee()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var granteeCharacterId = NextId();
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, granteeCharacterId, GranteeAccountId, "Grantee");

        await using var context = new CloudDbContext(options);
        var gateway = new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.SetAsync(ShardId, OwnerAccountId, "Grantee", CloudSharingGrantLevel.ViewOnly);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.AreEqual(CloudSharingGrantLevel.ViewOnly, outcome.Value!.Level);
        Assert.AreEqual(1, outcome.Value!.Version);

        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, OwnerAccountId);
        var granteeId = CloudOwnerIdentity.ForAccount(ShardId, GranteeAccountId);

        await using var verifyContext = new CloudDbContext(options);
        var stored = await verifyContext.CloudSharingGrants.AsNoTracking().SingleAsync(g => g.OwnerId == ownerId && g.GranteeId == granteeId);
        Assert.AreEqual(CloudSharingGrantLevel.ViewOnly, stored.Level);

        var notification = await verifyContext.CloudNotifications.AsNoTracking()
            .SingleOrDefaultAsync(n => n.OwnerId == granteeId && n.Kind == CloudNotificationKind.SharingGrantChanged);
        Assert.IsNotNull(notification, "Setting a grant must notify the grantee (EVT-003).");

        var ledgerEvent = await verifyContext.CloudSharingGrantLedgerEvents.AsNoTracking()
            .SingleOrDefaultAsync(e => e.OwnerId == ownerId && e.GranteeId == granteeId);
        Assert.IsNotNull(ledgerEvent, "Setting a grant must append an audit entry (EVT-001).");
        Assert.AreEqual(CloudSharingGrantLedgerEventType.LevelSet, ledgerEvent!.EventType);
    }

    [TestMethod]
    public async Task SetAsync_AnUnknownGranteeCharacter_IsRejected()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using var context = new CloudDbContext(options);
        var gateway = new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.SetAsync(ShardId, OwnerAccountId, "NoSuchCharacter", CloudSharingGrantLevel.ViewOnly);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task SetAsync_TheOwnersOwnCharacter_IsRejectedAsSelfGrantee()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerCharacterId = NextId();
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, ownerCharacterId, OwnerAccountId, "Owner");

        await using var context = new CloudDbContext(options);
        var gateway = new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.SetAsync(ShardId, OwnerAccountId, "Owner", CloudSharingGrantLevel.ViewOnly);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task SetAsync_TheSameLevelAgain_IsANoOpThatDoesNotAppendASecondLedgerEventOrNotification()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var granteeCharacterId = NextId();
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, granteeCharacterId, GranteeAccountId, "Grantee");

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context));
            var first = await gateway.SetAsync(ShardId, OwnerAccountId, "Grantee", CloudSharingGrantLevel.ViewOnly);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);
        }

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context));
            var second = await gateway.SetAsync(ShardId, OwnerAccountId, "Grantee", CloudSharingGrantLevel.ViewOnly);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
            Assert.AreEqual(1, second.Value!.Version, "A same-value re-send must not bump the version.");
        }

        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, OwnerAccountId);
        var granteeId = CloudOwnerIdentity.ForAccount(ShardId, GranteeAccountId);

        await using var verifyContext = new CloudDbContext(options);
        var ledgerCount = await verifyContext.CloudSharingGrantLedgerEvents.CountAsync(e => e.OwnerId == ownerId && e.GranteeId == granteeId);
        Assert.AreEqual(1, ledgerCount, "A no-op re-send must not append a second audit entry.");

        var notificationCount = await verifyContext.CloudNotifications.CountAsync(n => n.OwnerId == granteeId && n.Kind == CloudNotificationKind.SharingGrantChanged);
        Assert.AreEqual(1, notificationCount, "A no-op re-send must not create a second notification.");
    }

    [TestMethod]
    public async Task SetAsync_ExplicitNoneAfterViewOnly_IsARealChangeThatOverridesTheGrant()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var granteeCharacterId = NextId();
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, granteeCharacterId, GranteeAccountId, "Grantee");

        await using (var context = new CloudDbContext(options))
        {
            await new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context))
                .SetAsync(ShardId, OwnerAccountId, "Grantee", CloudSharingGrantLevel.ViewOnly);
        }

        await using var context2 = new CloudDbContext(options);
        var outcome = await new CloudSharingGrantGateway(context2, new CloudAccountLinkGateway(context2))
            .SetAsync(ShardId, OwnerAccountId, "Grantee", CloudSharingGrantLevel.None);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(CloudSharingGrantLevel.None, outcome.Value!.Level);
        Assert.AreEqual(2, outcome.Value!.Version);
    }

    [TestMethod]
    public async Task SetAsync_DowngradingFromViewAndWithdraw_ReleasesActiveGrantDerivedWithdrawalReservation()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var granteeCharacterId = NextId();
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, granteeCharacterId, GranteeAccountId, "Grantee");

        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, OwnerAccountId);
        var granteeId = CloudOwnerIdentity.ForAccount(ShardId, GranteeAccountId);

        Guid grantId;
        await using (var context = new CloudDbContext(options))
        {
            var setOutcome = await new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context))
                .SetAsync(ShardId, OwnerAccountId, "Grantee", CloudSharingGrantLevel.ViewAndWithdraw);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, setOutcome.Kind);
            grantId = setOutcome.Value!.Id;
        }

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        Guid reservationId;
        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

            var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
                [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, granteeId, grantId,
                NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
            reservationId = reserveOutcome.Value!.Id;
        }

        await using (var context = new CloudDbContext(options))
        {
            var downgradeOutcome = await new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context))
                .SetAsync(ShardId, OwnerAccountId, "Grantee", CloudSharingGrantLevel.ViewOnly);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, downgradeOutcome.Kind);
        }

        await using var verifyContext = new CloudDbContext(options);
        var reservation = await verifyContext.CloudWithdrawalReservations.AsNoTracking().SingleAsync(r => r.Id == reservationId);
        Assert.AreEqual(CloudReservationStatus.Released, reservation.Status);
        Assert.AreEqual(CloudReservationReleaseReason.SharingGrantAuthorityLost, reservation.ReleaseReason);
    }

    [TestMethod]
    public async Task ReserveForGrantedWithdrawal_ThenRedeem_BindsRedemptionAuthorityToTheGranteesOwnGroup()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var granteeCharacterId = NextId();
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, granteeCharacterId, GranteeAccountId, "Grantee");

        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, OwnerAccountId);
        var granteeId = CloudOwnerIdentity.ForAccount(ShardId, GranteeAccountId);

        Guid grantId;
        await using (var context = new CloudDbContext(options))
        {
            var setOutcome = await new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context))
                .SetAsync(ShardId, OwnerAccountId, "Grantee", CloudSharingGrantLevel.ViewAndWithdraw);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, setOutcome.Kind);
            grantId = setOutcome.Value!.Id;
        }

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        var tokenHash = NewTokenHash();
        var recipientContainerId = NextId();

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

            var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
                [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, granteeId, grantId,
                tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
            Assert.AreEqual(granteeId, reserveOutcome.Value!.RedeemerOwnerId, "A grant-derived reservation must record the grantee's identity as redeemer.");
            Assert.AreEqual(grantId, reserveOutcome.Value!.SharingGrantId);
            Assert.AreEqual(ownerId, reserveOutcome.Value!.OwnerId, "The asset owner identity must remain the actual owner, not the grantee.");
        }

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind, redeemOutcome.Reason);
        }
    }

    [TestMethod]
    public async Task RedeemWithdrawalReservation_TheGrantWasDowngradedOutOfBand_RefusesRedemptionAndReleasesTheReservation()
    {
        // Defense-in-depth commit-time revalidation (SHARE-004, WDR-008): even if a grant's level
        // changed through some path other than CloudSharingGrantGateway.SetAsync's own proactive
        // release (for example a narrow race, or -- as modeled here -- direct out-of-band state),
        // redemption itself must still refuse to deliver on lost authority.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var granteeCharacterId = NextId();
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, granteeCharacterId, GranteeAccountId, "Grantee");

        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, OwnerAccountId);
        var granteeId = CloudOwnerIdentity.ForAccount(ShardId, GranteeAccountId);

        Guid grantId;
        await using (var context = new CloudDbContext(options))
        {
            var setOutcome = await new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context))
                .SetAsync(ShardId, OwnerAccountId, "Grantee", CloudSharingGrantLevel.ViewAndWithdraw);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, setOutcome.Kind);
            grantId = setOutcome.Value!.Id;
        }

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        var tokenHash = NewTokenHash();

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

            var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
                [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, granteeId, grantId,
                tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
        }

        // Bypasses CloudSharingGrantGateway entirely, modeling out-of-band state so the reservation
        // is not proactively released -- the point of this test is the redemption-time check itself.
        await using (var connection = new MySqlConnection(_fixture.CloudConnectionString))
        {
            await connection.OpenAsync();
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE CloudSharingGrant SET Level = 'None' WHERE Id = @id;";
            update.Parameters.AddWithValue("@id", grantId.ToString());
            await update.ExecuteNonQueryAsync();
        }

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, NextId(), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, redeemOutcome.Kind);
        }

        await using var verifyContext = new CloudDbContext(options);
        var reservation = await verifyContext.CloudWithdrawalReservations.AsNoTracking().SingleAsync(r => r.SharingGrantId == grantId);
        Assert.AreEqual(CloudReservationStatus.Released, reservation.Status);
        Assert.AreEqual(CloudReservationReleaseReason.SharingGrantAuthorityLost, reservation.ReleaseReason);

        // The Cloud Custody Record must remain in Cloud custody -- lost authority must never deliver.
        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));
    }

    [TestMethod]
    public async Task GetEffectiveAccessAsync_TheOwnerViewingTheirOwnInventory_IsOwner()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var reader = new CloudSharingGrantReader(context);

        var access = await reader.GetEffectiveAccessAsync(ShardId, OwnerAccountId, OwnerAccountId);

        Assert.AreEqual(CloudSharingAccessLevel.Owner, access);
    }

    [TestMethod]
    public async Task GetEffectiveAccessAsync_NoGrantAndNoAllegiance_IsNone()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var reader = new CloudSharingGrantReader(context);

        var access = await reader.GetEffectiveAccessAsync(ShardId, OwnerAccountId, GranteeAccountId);

        Assert.AreEqual(CloudSharingAccessLevel.None, access);
    }

    [TestMethod]
    public async Task GetEffectiveAccessAsync_QualifyingCurrentAllegiance_DerivesViewOnly()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await PublishSharedAllegianceAsync(options, OwnerAccountId, GranteeAccountId, monarchId: 12345);

        await using var context = new CloudDbContext(options);
        var reader = new CloudSharingGrantReader(context);

        var access = await reader.GetEffectiveAccessAsync(ShardId, OwnerAccountId, GranteeAccountId);

        Assert.AreEqual(CloudSharingAccessLevel.ViewOnly, access);
    }

    [TestMethod]
    public async Task GetEffectiveAccessAsync_ExplicitNoneOverridesQualifyingCurrentAllegiance_ConflictingGrant()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await PublishSharedAllegianceAsync(options, OwnerAccountId, GranteeAccountId, monarchId: 22222);

        var granteeCharacterId = NextId();
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, granteeCharacterId, GranteeAccountId, "Grantee");

        await using (var context = new CloudDbContext(options))
        {
            await new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context))
                .SetAsync(ShardId, OwnerAccountId, "Grantee", CloudSharingGrantLevel.None);
        }

        await using var readContext = new CloudDbContext(options);
        var access = await new CloudSharingGrantReader(readContext).GetEffectiveAccessAsync(ShardId, OwnerAccountId, GranteeAccountId);

        Assert.AreEqual(CloudSharingAccessLevel.None, access, "Explicit None must override guild-derived access (SHARE-004).");
    }

    [TestMethod]
    public async Task GetEffectiveAccessAsync_UnrelatedThirdPartyWithNoAllegianceOverlap_IsNone()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await PublishSharedAllegianceAsync(options, OwnerAccountId, GranteeAccountId, monarchId: 33333);

        await using var context = new CloudDbContext(options);
        var reader = new CloudSharingGrantReader(context);

        var access = await reader.GetEffectiveAccessAsync(ShardId, OwnerAccountId, ThirdPartyAccountId);

        Assert.AreEqual(CloudSharingAccessLevel.None, access);
    }

    /// <summary>
    /// Publishes the ACE-side identity/allegiance outbox events for two accounts' characters to share
    /// one current monarch, and runs the projection consumer so the read-only cache
    /// <see cref="CloudSharingGrantReader"/> queries reflects it (mirrors
    /// <see cref="CloudIdentityProjectionConsumerTests"/>'s own established shape: a character event
    /// sets <c>AccountId</c>, an allegiance event sets <c>MonarchId</c>, and both must land on the
    /// same read-projection row before a shared-monarch query can find them).
    /// </summary>
    private async Task PublishSharedAllegianceAsync(
        Microsoft.EntityFrameworkCore.DbContextOptions<CloudDbContext> options, uint firstAccountId, uint secondAccountId, uint monarchId)
    {
        var firstCharacterId = NextId();
        var secondCharacterId = NextId();

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterRenamed, firstCharacterId, firstAccountId, "OwnerChar", totalLogins: 1, Guid.NewGuid());
            await gateway.PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterRenamed, secondCharacterId, secondAccountId, "GranteeChar", totalLogins: 1, Guid.NewGuid());
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceSworn, firstCharacterId, monarchId, priorMonarchId: null, Guid.NewGuid());
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceSworn, secondCharacterId, monarchId, priorMonarchId: null, Guid.NewGuid());
        }

        await using (var consumerContext = new CloudDbContext(options))
        {
            var consumer = new CloudIdentityProjectionConsumer(consumerContext);
            await consumer.RunBatchAsync(ShardId, maxCount: 100);
        }
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    private static string NewTokenHash() => CloudWithdrawalTokenHasher.Hash(Guid.NewGuid().ToString("N"));
}
