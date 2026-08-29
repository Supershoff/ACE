using System;
using System.Collections.Generic;
using System.Linq;

using log4net;

using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Common;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Factories;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// The world-thread Withdrawal Token redemption handler (AC Cloud Mule issue #16, WDR-001..
    /// WDR-008, INV-002, INV-003, ARCH-002). A Withdrawal Token is created off-world (a web selection,
    /// WDR-001) through <see cref="CloudCustodyBoundary.ReserveForWithdrawalAsync"/> or
    /// <see cref="CloudCustodyBoundary.ReserveStackLotForWithdrawalAsync"/>; this class is exclusively
    /// what redeems it, because only ACE may materialize/deliver a Cloud Item back to the playable
    /// world (ARCH-002).
    ///
    /// Redemption order matters (WDR-003: "failures deliver nothing and retain a retryable
    /// reservation"): every safe-state (WDR-004), location (WDR-006), and native-receive capacity
    /// (WDR-005) check runs and must pass <em>before</em> the Cloud persistence boundary is ever
    /// called, so a rejected redemption never touches the reservation's custody-to-world transition at
    /// all. Only after that boundary call commits does this class place and network the delivered item
    /// through ACE's ordinary <see cref="Player_Inventory.TryCreateInInventoryWithNetworking(WorldObject)"/>
    /// receive path -- the same slots/burden/side-pack placement every other ordinary item transfer
    /// uses.
    /// </summary>
    partial class Player
    {
        private static readonly ILog cloudWithdrawalLog = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Redeems one Withdrawal Token, identified by the high-entropy secret the player
        /// typed/pasted from their web selection (WDR-001). Reports an exact, actionable reason for
        /// every rejection (WDR-005) and never delivers a partial result (WDR-003).
        /// </summary>
        public void HandleCloudWithdrawalRedeem(string tokenSecret)
        {
            if (!ConfigManager.Config.CloudMule.Enabled)
            {
                SendTransientError("AC Cloud Mule is disabled.");
                return;
            }

            var shardId = ConfigManager.Config.CloudMule.ShardId;
            if (string.IsNullOrWhiteSpace(shardId))
            {
                SendTransientError("AC Cloud Mule is enabled but CloudMule.ShardId is not configured.");
                return;
            }

            if (string.IsNullOrWhiteSpace(tokenSecret))
            {
                SendTransientError("Usage: @withdraw <token>");
                return;
            }

            TryRunCloudWithdrawalRedeem(
                () =>
                {
                    // WDR-004, revalidated again by the boundary's own commit-time locking for the
                    // custody side; this first check exists so an obviously-unsafe redemption never
                    // even attempts the Cloud round trip.
                    var safeStateResult = CloudWithdrawalSafeStatePolicy.Evaluate(BuildWithdrawalSafeStateSnapshot());
                    if (!safeStateResult.IsSafe)
                    {
                        SendTransientError(safeStateResult.Reason!);
                        return;
                    }

                    // WDR-006.
                    if (!CloudWithdrawalLocationPolicy.IsEligible(BuildWithdrawalLocationSnapshot(shardId)))
                    {
                        SendTransientError("You cannot redeem a Withdrawal Token here.");
                        return;
                    }

                    var tokenHash = CloudWithdrawalTokenHasher.Hash(tokenSecret);

                    RedeemAsync(shardId, tokenHash).GetAwaiter().GetResult();
                },
                ex =>
                {
                    cloudWithdrawalLog.Error($"[CLOUD WITHDRAWAL] Redemption for player {Name} threw.", ex);
                    SendTransientError("That Withdrawal Token could not be redeemed. Please try again.");
                });
        }

        /// <summary>
        /// Runs <paramref name="redeem"/> and reports any exception it throws to
        /// <paramref name="onException"/> instead of letting it propagate (AC Cloud Mule review of
        /// issue #16/PR #111, finding [P1]: an exception from the safe-state, location, token-hash,
        /// or owner-identity checks used to escape this method's try/catch -- which only wrapped
        /// <see cref="RedeemAsync"/> -- straight into <c>GameActionTalk.Handle</c>'s generic
        /// command-exception handler, which logs the raw command text, i.e. the plaintext Withdrawal
        /// Token secret, to the server's Error log). Kept free of any live Session/Player dependency
        /// so it can run in a unit test.
        /// </summary>
        internal static void TryRunCloudWithdrawalRedeem(Action redeem, Action<Exception> onException)
        {
            try
            {
                redeem();
            }
            catch (Exception ex)
            {
                onException(ex);
            }
        }

        /// <summary>
        /// WDR-002's ownership-group-aware "belongs to your account" check (AC Cloud Mule review of
        /// PR #120, finding [P1]: comparing a reservation's owner identity directly against the
        /// redeeming account's own identity rejected every redemption across a Main/Linked link, since
        /// linking never rewrites an already-open reservation's <c>OwnerId</c>). True when any account
        /// in <paramref name="groupAccountIds"/> -- the redeemer's current ownership group, resolved
        /// by <see cref="CloudAccountLinkGateway.GetOwnershipGroupAccountIdsAsync"/> -- is the account
        /// <paramref name="reservationOwnerId"/> was computed for. Kept pure and free of any live
        /// Session/Player/database dependency so it is directly unit-testable (mirrors
        /// <see cref="TryRunCloudWithdrawalRedeem"/>'s same seam).
        /// </summary>
        internal static bool BelongsToRedeemersOwnershipGroup(string shardId, Guid reservationOwnerId, IEnumerable<uint> groupAccountIds) =>
            groupAccountIds.Any(groupAccountId => CloudOwnerIdentity.ForAccount(shardId, groupAccountId) == reservationOwnerId);

        private async System.Threading.Tasks.Task RedeemAsync(string shardId, string tokenHash)
        {
            using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString()));
            var boundary = new CloudCustodyBoundary(context);

            var wholeItemReservation = await boundary.TryGetActiveWithdrawalReservationAsync(tokenHash);
            var stackLotReservation = wholeItemReservation is null
                ? await boundary.TryGetActiveStackLotWithdrawalReservationAsync(tokenHash)
                : null;

            if (wholeItemReservation is null && stackLotReservation is null)
            {
                SendTransientError("That Withdrawal Token is invalid, expired, or already used.");
                return;
            }

            // WDR-002: "cannot be redeemed by an unrelated account," where CONTEXT.md defines "the
            // owner's group" as the redeeming account's current Main/Linked ownership group
            // (AUTH-005..009) -- not merely a byte-for-byte match against the redeeming account's own
            // identity, since a reservation opened under either the Main Account's or a Linked
            // Account's identity must remain redeemable by any character in that same group once the
            // two are linked.
            var reservationOwnerId = wholeItemReservation?.OwnerId ?? stackLotReservation!.OwnerId;
            var groupAccountIds = await new CloudAccountLinkGateway(context).GetOwnershipGroupAccountIdsAsync(shardId, Session.AccountId);
            if (!BelongsToRedeemersOwnershipGroup(shardId, reservationOwnerId, groupAccountIds))
            {
                SendTransientError("That Withdrawal Token does not belong to your account.");
                return;
            }

            if (wholeItemReservation is not null)
            {
                await RedeemWholeItemAsync(boundary, tokenHash, wholeItemReservation);
            }
            else
            {
                await RedeemStackLotAsync(boundary, tokenHash, stackLotReservation!);
            }
        }

        private async System.Threading.Tasks.Task RedeemWholeItemAsync(
            CloudCustodyBoundary boundary, string tokenHash, CloudWithdrawalReservation reservation)
        {
            var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(reservation.BiotaId);
            if (biota is null)
            {
                SendTransientError("That Withdrawal Token's item could not be found.");
                return;
            }

            var prospectiveItem = WorldObjectFactory.CreateWorldObject(biota);
            if (!PassesNativeReceiveCapacityCheck(prospectiveItem))
            {
                return;
            }

            var outcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, Guid.Full, System.Guid.NewGuid());
            if (outcome.Kind != CloudBoundaryOutcomeKind.Committed)
            {
                cloudWithdrawalLog.Warn($"[CLOUD WITHDRAWAL] Whole-item redemption for player {Name} was not committed: {outcome.Reason}");
                SendTransientError(outcome.Reason ?? "That Withdrawal Token could not be redeemed. Please try again.");
                return;
            }

            DeliverRedeemedItem(outcome.Value!.BiotaId);
        }

        private async System.Threading.Tasks.Task RedeemStackLotAsync(
            CloudCustodyBoundary boundary, string tokenHash, CloudStackLotWithdrawalReservation reservation)
        {
            var preview = await boundary.PreviewStackLotWithdrawalAsync(reservation.LotId);
            if (preview is null)
            {
                SendTransientError("That Withdrawal Token's item could not be found.");
                return;
            }

            var originalBiota = DatabaseManager.Shard.BaseDatabase.GetBiota(preview.BackingBiotaId);
            if (originalBiota is null)
            {
                SendTransientError("That Withdrawal Token's item could not be found.");
                return;
            }

            // Informational only (CloudStackLotWithdrawalPreview's doc comment): used solely to decide
            // whether to pre-allocate a materialized child GUID (ARCH-010) and to build a capacity
            // pre-check representative of the actually delivered item. RedeemStackLotWithdrawalReservationAsync
            // re-derives the real answer fresh under its own row lock and refuses the request if this
            // guess turns out wrong -- a legitimate, retryable Conflict, never a custody violation.
            uint? materializedBiotaId = null;
            WorldObject prospectiveItem;

            if (preview.IsSoleLotOnStack)
            {
                prospectiveItem = WorldObjectFactory.CreateWorldObject(originalBiota);
            }
            else
            {
                var originalItem = WorldObjectFactory.CreateWorldObject(originalBiota);
                var weenie = DatabaseManager.World.GetCachedWeenie(originalItem.WeenieClassId);
                if (weenie is null)
                {
                    SendTransientError("That Withdrawal Token's item could not be found.");
                    return;
                }

                var guid = GuidManager.NewDynamicGuid();
                materializedBiotaId = guid.Full;
                prospectiveItem = WorldObjectFactory.CreateWorldObject(weenie, guid);
                prospectiveItem.SetProperty(PropertyInt.StackSize, reservation.Quantity);
            }

            if (!PassesNativeReceiveCapacityCheck(prospectiveItem))
            {
                if (materializedBiotaId.HasValue)
                {
                    GuidManager.RecycleDynamicGuid(new ObjectGuid(materializedBiotaId.Value));
                }

                return;
            }

            var outcome = await boundary.RedeemStackLotWithdrawalReservationAsync(
                tokenHash, Guid.Full, materializedBiotaId, System.Guid.NewGuid());

            if (outcome.Kind != CloudBoundaryOutcomeKind.Committed)
            {
                cloudWithdrawalLog.Warn($"[CLOUD WITHDRAWAL] Stack Lot redemption for player {Name} was not committed: {outcome.Reason}");
                SendTransientError(outcome.Reason ?? "That Withdrawal Token could not be redeemed. Please try again.");

                if (materializedBiotaId.HasValue)
                {
                    GuidManager.RecycleDynamicGuid(new ObjectGuid(materializedBiotaId.Value));
                }

                return;
            }

            if (materializedBiotaId.HasValue && outcome.Value!.DeliveredBiotaId != materializedBiotaId.Value)
            {
                // The boundary's own locked recheck disagreed with our unlocked preview (a sibling
                // lot was removed between the preview and the redeem, making this a full-stack
                // delivery after all) -- the pre-allocated GUID went unused and must not leak.
                GuidManager.RecycleDynamicGuid(new ObjectGuid(materializedBiotaId.Value));
            }

            DeliverRedeemedItem(outcome.Value!.DeliveredBiotaId);
        }

        /// <summary>
        /// WDR-005's slot/burden/uniqueness half of ACE's native receive validation, run <em>before</em>
        /// the Cloud persistence boundary is ever called (WDR-003: a capacity failure must never
        /// consume the reservation). <see cref="Container.CanAddToInventory(WorldObject)"/> is the
        /// same non-mutating slot/burden check <see cref="Container.TryAddToInventory(WorldObject, out Container, int, bool, bool)"/>
        /// performs before it ever mutates anything, and <see cref="CheckUniques(WorldObject, WorldObject)"/>
        /// is the same check ordinary pickup uses. Native stack merges are not a distinct case here:
        /// ACE's ordinary receive path (vendor purchase, loot, this) never auto-merges a newly
        /// received stack into an existing one -- it always places a new pack entry -- so there is no
        /// separate merge behavior to reproduce beyond ordinary placement.
        /// </summary>
        private bool PassesNativeReceiveCapacityCheck(WorldObject prospectiveItem)
        {
            if (!CanAddToInventory(prospectiveItem))
            {
                SendTransientError($"You do not have enough room or are too encumbered to receive {prospectiveItem.Name} right now. Your Withdrawal Token remains valid; try again once you have space.");
                return false;
            }

            if (prospectiveItem.IsUniqueOrContainsUnique && !CheckUniques(prospectiveItem))
            {
                SendTransientError($"You already have a unique {prospectiveItem.Name} and cannot receive another. Your Withdrawal Token remains valid until it expires.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Places and networks an already-committed redemption's delivered biota through ACE's
        /// ordinary receive path. The Cloud persistence boundary already recorded this biota's world
        /// possession under the player's own top-level container GUID as a crash-safe placeholder;
        /// <see cref="Player_Inventory.TryCreateInInventoryWithNetworking(WorldObject)"/> immediately
        /// re-persists it under whichever exact main-pack/side-pack container it actually chooses, so
        /// there is no window in which the committed state is anything other than "safely in this
        /// player's possession."
        /// </summary>
        private void DeliverRedeemedItem(uint deliveredBiotaId)
        {
            var deliveredBiota = DatabaseManager.Shard.BaseDatabase.GetBiota(deliveredBiotaId, doNotAddToCache: true);
            if (deliveredBiota is null)
            {
                cloudWithdrawalLog.Error($"[CLOUD WITHDRAWAL] Committed redemption for player {Name} delivered biota 0x{deliveredBiotaId:X8}, but it could not be reloaded.");
                SendTransientError("Your Withdrawal Token was redeemed, but the item could not be delivered. Please contact an administrator.");
                return;
            }

            var deliveredItem = WorldObjectFactory.CreateWorldObject(deliveredBiota);

            if (!TryCreateInInventoryWithNetworking(deliveredItem))
            {
                // The committed boundary transaction already moved this biota out of Cloud custody
                // into this player's world possession (their own top-level container); a placement
                // failure here is a narrow post-commit race, not a lost or duplicated item. Nothing
                // further to roll back -- ARCH-005 already treats this as valid persisted world state.
                cloudWithdrawalLog.Error($"[CLOUD WITHDRAWAL] Failed to place committed redemption 0x{deliveredBiotaId:X8} into player {Name}'s inventory after capacity had already been checked.");
                SendTransientError("Your Withdrawal Token was redeemed, but the item could not be placed in your inventory. Please contact an administrator.");
                return;
            }

            Session.Network.EnqueueSend(new ACE.Server.Network.GameMessages.Messages.GameMessageSystemChat(
                $"You have redeemed {deliveredItem.Name} from Cloud Mule.", ACE.Entity.Enum.ChatMessageType.Broadcast));
        }

        /// <summary>
        /// Maps live Player state to WDR-004's safe-state facts. IsBusy is ACE's general
        /// "currently performing another action" flag (set, for example, alongside IsLoggingOut during
        /// logout and checked alongside Teleporting elsewhere) -- the same flag that already guards
        /// every other ordinary inventory transfer, so it stands in for "performing another inventory
        /// transfer."
        /// </summary>
        private CloudWithdrawalSafeStateSnapshot BuildWithdrawalSafeStateSnapshot() => new(
            IsAlive: IsAlive,
            IsFullyLoaded: CurrentLandblock is not null,
            IsInCombatMode: CombatMode != CombatMode.NonCombat,
            IsTrading: IsTrading,
            IsTeleporting: Teleporting,
            IsLoggingOut: IsLoggingOut,
            IsPerformingAnotherTransfer: IsBusy);

        /// <summary>
        /// Maps this player's live position plus this shard's Marketplace/housing content and
        /// administrator-managed Withdrawal Landblock configuration to WDR-006's eligibility facts.
        /// </summary>
        private CloudWithdrawalLocationSnapshot BuildWithdrawalLocationSnapshot(string shardId)
        {
            var landblock = Location.LandblockId.Landblock;

            var isMarketplace = landblock == ResolveMarketplaceLandblock();
            var isHousing = IsHousingLandblock(landblock);

            var withdrawAnywhereEnabled = false;
            var isNamedWithdrawalLandblock = false;

            try
            {
                using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString()));
                var boundary = new CloudWithdrawalLocationConfigurationBoundary(context);
                var configuration = boundary.GetCurrentAsync(shardId).GetAwaiter().GetResult();

                withdrawAnywhereEnabled = configuration.WithdrawAnywhereEnabled;
                isNamedWithdrawalLandblock = configuration.NamedLandblocks.Any(l => l.Landblock == landblock);
            }
            catch (Exception ex)
            {
                cloudWithdrawalLog.Error("[CLOUD WITHDRAWAL] Failed to read Withdrawal Location configuration; treating withdraw-anywhere/named landblocks as unavailable for this attempt.", ex);
            }

            return new CloudWithdrawalLocationSnapshot(isMarketplace, isHousing, isNamedWithdrawalLandblock, withdrawAnywhereEnabled);
        }

        /// <summary>
        /// Mirrors <c>CloudCustodianManager.ResolveMarketplacePosition</c>'s exact fallback (the
        /// hardcoded landblock ACE's own <c>@marketplace</c> teleport already uses when the
        /// "portalmarketplace" weenie is absent), so Cloud Mule's notion of "the Marketplace" always
        /// matches wherever ordinary teleport already sends players on this shard.
        /// </summary>
        private static ushort ResolveMarketplaceLandblock()
        {
            var weenie = DatabaseManager.World.GetCachedWeenie("portalmarketplace");
            var position = weenie?.GetPosition(PositionType.Destination) ?? new Position(0x016C01BC, 49.206f, -31.935f, 0.005f, 0, 0, -0.707107f, 0.707107f);
            return position.LandblockId.Landblock;
        }

        /// <summary>
        /// True when <paramref name="landblock"/> contains any player housing (WDR-006: "any
        /// landblock containing player housing/SlumLord"), resolved from this shard's own live
        /// ace_world content -- there is no fixed, shippable list of housing landblocks, mirroring
        /// <c>CloudCustodianManager.ResolveMansionLocations</c>'s same approach for Mansion-tier
        /// Custodian locations, generalized here to every House-type placement rather than only
        /// Mansions.
        /// </summary>
        private static bool IsHousingLandblock(ushort landblock)
        {
            try
            {
                using var context = new ACE.Database.Models.World.WorldDbContext();

                var minCellId = (uint)landblock << 16;
                var maxCellId = minCellId | 0xFFFF;

                var hasHousing =
                    (from weenie in context.Weenie
                     join instance in context.LandblockInstance on weenie.ClassId equals instance.WeenieClassId
                     where weenie.Type == (int)WeenieType.House
                        && instance.ObjCellId >= minCellId && instance.ObjCellId <= maxCellId
                     select instance.Guid)
                    .Any();

                return hasHousing;
            }
            catch (Exception ex)
            {
                cloudWithdrawalLog.Error($"[CLOUD WITHDRAWAL] Failed to determine whether landblock 0x{landblock:X4} contains player housing; treating it as not eligible for this attempt.", ex);
                return false;
            }
        }
    }
}
