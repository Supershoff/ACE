using System;
using System.Collections.Generic;

using log4net;

using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Common;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// The dedicated Cloud Custodian deposit handler (AC Cloud Mule issue #13, DEP-001..005, DEP-009,
    /// ARCH-002, ARCH-005): <see cref="Player_Commerce.HandleActionSellItem"/> routes every
    /// <see cref="CloudCustodian"/> sale here instead of the ordinary
    /// <see cref="Vendor.ProcessItemsForPurchase"/> resell/destroy path, because a Cloud deposit must
    /// atomically replace world possession with a Cloud Custody Record rather than resell or destroy
    /// the item.
    /// </summary>
    partial class Player
    {
        private static readonly ILog cloudCustodianLog = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Validates and commits every submitted row independently (DEP-002): a row that fails
        /// eligibility, quantity, or duplicate checks -- or whose deposit is rejected by the Cloud
        /// persistence boundary -- stays with the player and reports its exact reason, while every
        /// other valid row in the same submission still deposits.
        /// </summary>
        public void HandleCloudCustodianDeposit(CloudCustodian custodian, List<ItemProfile> itemProfiles)
        {
            var shardId = ConfigManager.Config.CloudMule.ShardId;

            var allPossessions = GetAllPossessions();
            var possessionsByGuid = new Dictionary<uint, WorldObject>();
            foreach (var wo in allPossessions)
            {
                possessionsByGuid[wo.Guid.Full] = wo;
            }

            var seenGuids = new HashSet<uint>();
            var depositedCount = 0;

            foreach (var itemProfile in itemProfiles)
            {
                if (!possessionsByGuid.TryGetValue(itemProfile.ObjectGuid, out var item))
                {
                    cloudCustodianLog.Warn(
                        $"[CLOUD CUSTODIAN] {Name} tried to deposit item 0x{itemProfile.ObjectGuid:X8} not in their possession to {custodian.Name}.");
                    continue;
                }

                var isDuplicate = !seenGuids.Add(item.Guid.Full);

                var saleWindow = CloudCustodianManager.ValidateSaleWindow(custodian);

                var request = new CloudCustodianDepositRowRequest(
                    new CloudItemId(item.Guid.Full),
                    itemProfile.Amount,
                    currentStackSize: item.StackSize ?? 1,
                    isStackable: item.StackSize.HasValue,
                    isDuplicateInSubmission: isDuplicate,
                    BuildEligibilitySnapshot(item));

                var decision = CloudCustodianDepositRowPolicy.Decide(request, saleWindow);

                if (decision.Kind == CloudCustodianDepositRowDecisionKind.Reject)
                {
                    SendTransientError(decision.PlayerMessage);
                    continue;
                }

                if (TryDepositRow(custodian, item, decision, shardId))
                {
                    depositedCount++;
                }
            }

            if (depositedCount == 0)
            {
                Session.Network.EnqueueSend(new GameEventInventoryServerSaveFailed(Session, Guid.Full));
                SendUseDoneEvent();
                return;
            }

            Session.Network.EnqueueSend(new GameMessageSound(Guid, Sound.PickUpItem));
            SendUseDoneEvent();
        }

        /// <summary>
        /// Removes <paramref name="item"/> from this player's possession, durably persists that
        /// removal to ace_shard synchronously (the Cloud persistence boundary's precondition reads
        /// ace_shard directly, so the queued/async save path is not sufficient here), then commits
        /// the custody transition through <see cref="CloudCustodyBoundary"/>. Any failure at any step
        /// restores the item to the player rather than leaving it destroyed, resold, or ambiguously
        /// owned (DEP-002, ARCH-002, ARCH-005).
        /// </summary>
        private bool TryDepositRow(CloudCustodian custodian, WorldObject item, CloudCustodianDepositRowDecision decision, string shardId)
        {
            // Equipped items are already rejected by eligibility (DEP-003:
            // CloudEligibilityRejectionCode.MustBeInOrdinaryInventory) before this method is ever
            // called, so only the ordinary-inventory removal path applies here.
            if (!TryRemoveFromInventoryWithNetworking(item.Guid, out _, RemoveFromInventoryAction.SellItem))
            {
                cloudCustodianLog.Warn($"[CLOUD CUSTODIAN] Item 0x{item.Guid.Full:X8}:{item.Name} for player {Name} not found in HandleCloudCustodianDeposit.");
                SendTransientError("That item could not be removed from your possession.");
                return false;
            }

            if (!SynchronouslyPersist(item))
            {
                RestoreCloudDepositCandidate(item);
                SendTransientError($"A database error prevented depositing the {item.Name}.");
                return false;
            }

            var ownerId = CloudOwnerIdentity.ForAccount(shardId, Session.AccountId);
            var idempotencyKey = CloudOwnerIdentity.DepositIdempotencyKey(shardId, item.Guid.Full);

            CloudBoundaryOutcomeKind outcomeKind;
            string reason = null;

            try
            {
                using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString()));
                var boundary = new CloudCustodyBoundary(context);

                if (decision.Kind == CloudCustodianDepositRowDecisionKind.DepositStack)
                {
                    var outcome = boundary.DepositStackAsync(
                        item.Guid.Full, shardId, ownerId, decision.Quantity, idempotencyKey,
                        preservationRequirements: decision.PreservationRequirements).GetAwaiter().GetResult();
                    outcomeKind = outcome.Kind;
                    reason = outcome.Reason;
                }
                else
                {
                    var outcome = boundary.DepositAsync(
                        item.Guid.Full, shardId, ownerId, idempotencyKey,
                        preservationRequirements: decision.PreservationRequirements).GetAwaiter().GetResult();
                    outcomeKind = outcome.Kind;
                    reason = outcome.Reason;
                }
            }
            catch (Exception ex)
            {
                cloudCustodianLog.Error($"[CLOUD CUSTODIAN] Deposit of 0x{item.Guid.Full:X8}:{item.Name} for player {Name} threw.", ex);
                outcomeKind = CloudBoundaryOutcomeKind.Unavailable;
            }

            if (outcomeKind != CloudBoundaryOutcomeKind.Committed)
            {
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    cloudCustodianLog.Warn($"[CLOUD CUSTODIAN] Deposit of 0x{item.Guid.Full:X8}:{item.Name} for player {Name} was not committed: {reason}");
                }

                RestoreCloudDepositCandidate(item);
                SendTransientError($"The Cloud Custodian could not accept the {item.Name}. Please try again.");
                return false;
            }

            Session.Network.EnqueueSend(new GameEventItemServerSaysContainId(Session, item, custodian));
            return true;
        }

        private bool SynchronouslyPersist(WorldObject item)
        {
            item.SaveBiotaToDatabase(false);
            return DatabaseManager.Shard.BaseDatabase.SaveBiota(item.Biota, item.BiotaDatabaseLock);
        }

        private void RestoreCloudDepositCandidate(WorldObject item)
        {
            if (!TryCreateInInventoryWithNetworking(item))
            {
                cloudCustodianLog.Error(
                    $"[CLOUD CUSTODIAN] Failed to restore item 0x{item.Guid.Full:X8}:{item.Name} to player {Name} after a rejected Cloud deposit.");
                return;
            }

            SynchronouslyPersist(item);
        }

        /// <summary>
        /// Maps a live ACE <c>WorldObject</c> to the pure facts <see cref="CloudItemEligibilityPolicy"/>
        /// needs (DEP-003, DEP-004). <see cref="CloudItemEligibilitySnapshot.HasActiveCooldownOrAttachment"/>
        /// always resolves to false: no ACE item currently exposes generic, item-instance-scoped
        /// active-cooldown state outside pet devices, which <see cref="CloudItemEligibilitySnapshot.HasActivePetAttached"/>
        /// already covers; this flag is reserved for a future cooldown-bearing item type.
        /// </summary>
        private CloudItemEligibilitySnapshot BuildEligibilitySnapshot(WorldObject item)
        {
            var isCurrentlyTradedOrReserved = IsTrading && item.IsBeingTradedOrContainsItemBeingTraded(ItemsInTradeWindow);

            return new CloudItemEligibilitySnapshot(
                new CloudItemId(item.Guid.Full),
                isLegalForPlayerToPlayerTrade: !item.Retained,
                isEquipped: item.CurrentWieldedLocation.HasValue,
                isContainer: item is Container,
                isAttunedOrContainsAttuned: item.IsAttunedOrContainsAttuned,
                hasActivePetAttached: item is PetDevice petDevice && petDevice.Pet.HasValue,
                isCharacterBoundOrUnsafeStateful: item.Bonded is BondedStatus.Bonded or BondedStatus.Sticky,
                hasFiniteLifespan: item.Lifespan.HasValue,
                hasActiveCooldownOrAttachment: false,
                isCurrentlyTradedOrReserved: isCurrentlyTradedOrReserved,
                runtimeEnchantments: BuildRuntimeEnchantments(item.Biota.PropertiesEnchantmentRegistry.Clone(item.BiotaDatabaseLock)));
        }

        /// <summary>
        /// Reduces a live item's active enchantment registry to the Frozen Enchantment preservation
        /// list DEP-005 requires (<see cref="CloudItemEligibilitySnapshot.RuntimeEnchantments"/>):
        /// only runtime (temporary) enchantments, each with its currently remaining duration
        /// (Duration + StartTime -- StartTime ticks backwards toward -Duration every heartbeat, see
        /// <c>PropertiesEnchantmentRegistryExtensions.HeartBeatEnchantmentsAndReturnExpired</c>).
        /// Permanent built-in item spells (Duration == -1, an equip-linked enchantment that lasts
        /// only as long as the item stays equipped) and cooldown pseudo-entries (SpellId greater
        /// than <see cref="short.MaxValue"/>, the same range <c>EnchantmentManager.GetCooldownSpellID</c>
        /// reserves) are excluded, matching <c>EnchantmentManager.RemoveAllEnchantments</c>'s own
        /// exclusion pattern -- DEP-005: "Permanent built-in spells remain ordinary static
        /// properties." Kept free of any live <c>WorldObject</c>/<c>EnchantmentManager</c> dependency
        /// (only the plain registry entries it is handed) so it can run in a table-driven unit test.
        /// </summary>
        internal static IReadOnlyList<CloudRuntimeEnchantmentSnapshot> BuildRuntimeEnchantments(
            IEnumerable<PropertiesEnchantmentRegistry> activeEnchantments)
        {
            if (activeEnchantments is null)
            {
                return [];
            }

            var preserved = new List<CloudRuntimeEnchantmentSnapshot>();

            foreach (var entry in activeEnchantments)
            {
                if (entry.Duration == -1 || entry.SpellId > short.MaxValue)
                {
                    continue;
                }

                var remainingDurationSeconds = entry.Duration + entry.StartTime;
                if (remainingDurationSeconds <= 0)
                {
                    continue;
                }

                preserved.Add(new CloudRuntimeEnchantmentSnapshot(entry.SpellId, remainingDurationSeconds));
            }

            return preserved;
        }
    }
}
