using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
            var pendingDeposits = new List<PendingCloudDeposit>();

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

                var pending = PrepareCloudDepositRow(item, decision, shardId);
                if (pending != null)
                {
                    pendingDeposits.Add(pending);
                }
            }

            var depositedCount = pendingDeposits.Count == 0 ? 0 : CommitPendingCloudDeposits(custodian, pendingDeposits);

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
        /// The local half of a deposit row that has cleared eligibility (DEP-002, ARCH-005): removes
        /// <paramref name="item"/> from this player's possession and durably persists that removal to
        /// ace_shard synchronously (the Cloud persistence boundary's precondition reads ace_shard
        /// directly, so the queued/async save path is not sufficient here). Kept separate from the
        /// Cloud custody call itself (<see cref="DepositRowToCloudAsync"/>) so every prepared row's
        /// Cloud-DB round trip can run concurrently instead of one at a time (AC Cloud Mule review of
        /// issue #13, finding 3: sequential per-row Cloud calls stalled the whole world tick for the
        /// cumulative round-trip time of every row in the submission).
        /// </summary>
        private PendingCloudDeposit PrepareCloudDepositRow(WorldObject item, CloudCustodianDepositRowDecision decision, string shardId)
        {
            // Equipped items are already rejected by eligibility (DEP-003:
            // CloudEligibilityRejectionCode.MustBeInOrdinaryInventory) before this method is ever
            // called, so only the ordinary-inventory removal path applies here.
            if (!TryRemoveFromInventoryWithNetworking(item.Guid, out _, RemoveFromInventoryAction.SellItem))
            {
                cloudCustodianLog.Warn($"[CLOUD CUSTODIAN] Item 0x{item.Guid.Full:X8}:{item.Name} for player {Name} not found in HandleCloudCustodianDeposit.");
                SendTransientError("That item could not be removed from your possession.");
                return null;
            }

            if (!SynchronouslyPersist(item))
            {
                RestoreCloudDepositCandidate(item);
                SendTransientError($"A database error prevented depositing the {item.Name}.");
                return null;
            }

            return new PendingCloudDeposit(
                item,
                decision,
                shardId,
                CloudOwnerIdentity.ForAccount(shardId, Session.AccountId),
                CloudOwnerIdentity.DepositIdempotencyKey(shardId, item.Guid.Full));
        }

        /// <summary>
        /// Commits every prepared row's Cloud custody call concurrently rather than one at a time
        /// (<see cref="RunConcurrentlyAsync{T}"/>): each row targets a distinct biota and idempotency
        /// key, so running them together is safe and bounds this submission's Cloud-side world-tick
        /// stall to roughly one round trip instead of one per row.
        /// </summary>
        private int CommitPendingCloudDeposits(CloudCustodian custodian, List<PendingCloudDeposit> pendingDeposits)
        {
            var operations = pendingDeposits
                .Select(pending => (Func<Task<(CloudBoundaryOutcomeKind Kind, string Reason)>>)(() => DepositRowToCloudAsync(pending)))
                .ToList();

            var outcomes = RunConcurrentlyAsync(operations).GetAwaiter().GetResult();

            var depositedCount = 0;

            for (var i = 0; i < pendingDeposits.Count; i++)
            {
                var pending = pendingDeposits[i];
                var (outcomeKind, reason) = outcomes[i];

                if (outcomeKind != CloudBoundaryOutcomeKind.Committed)
                {
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        cloudCustodianLog.Warn($"[CLOUD CUSTODIAN] Deposit of 0x{pending.Item.Guid.Full:X8}:{pending.Item.Name} for player {Name} was not committed: {reason}");
                    }

                    RestoreCloudDepositCandidate(pending.Item);
                    SendTransientError($"The Cloud Custodian could not accept the {pending.Item.Name}. Please try again.");
                    continue;
                }

                Session.Network.EnqueueSend(new GameEventItemServerSaysContainId(Session, pending.Item, custodian));
                depositedCount++;
            }

            return depositedCount;
        }

        private async Task<(CloudBoundaryOutcomeKind Kind, string Reason)> DepositRowToCloudAsync(PendingCloudDeposit pending)
        {
            try
            {
                using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString()));
                var boundary = new CloudCustodyBoundary(context);

                if (pending.Decision.Kind == CloudCustodianDepositRowDecisionKind.DepositStack)
                {
                    var outcome = await boundary.DepositStackAsync(
                        pending.Item.Guid.Full, pending.ShardId, pending.OwnerId, pending.Decision.Quantity, pending.IdempotencyKey,
                        preservationRequirements: pending.Decision.PreservationRequirements);
                    return (outcome.Kind, outcome.Reason);
                }
                else
                {
                    var outcome = await boundary.DepositAsync(
                        pending.Item.Guid.Full, pending.ShardId, pending.OwnerId, pending.IdempotencyKey,
                        preservationRequirements: pending.Decision.PreservationRequirements);
                    return (outcome.Kind, outcome.Reason);
                }
            }
            catch (Exception ex)
            {
                cloudCustodianLog.Error($"[CLOUD CUSTODIAN] Deposit of 0x{pending.Item.Guid.Full:X8}:{pending.Item.Name} for player {Name} threw.", ex);
                return (CloudBoundaryOutcomeKind.Unavailable, null);
            }
        }

        /// <summary>
        /// Starts every operation before awaiting any of them, so the whole batch runs concurrently
        /// instead of one at a time. Kept free of any live WorldObject/Player dependency so it can run
        /// in a table-driven unit test (AC Cloud Mule review of issue #13, finding 3).
        /// </summary>
        internal static async Task<IReadOnlyList<T>> RunConcurrentlyAsync<T>(IReadOnlyList<Func<Task<T>>> operations)
        {
            var tasks = new Task<T>[operations.Count];
            for (var i = 0; i < operations.Count; i++)
            {
                tasks[i] = operations[i]();
            }

            return await Task.WhenAll(tasks);
        }

        private sealed record PendingCloudDeposit(
            WorldObject Item,
            CloudCustodianDepositRowDecision Decision,
            string ShardId,
            Guid OwnerId,
            Guid IdempotencyKey);

        /// <summary>
        /// Runs <paramref name="persist"/> and reports any exception it throws to
        /// <paramref name="onException"/> instead of letting it propagate, so a caller's existing
        /// "did the synchronous persist succeed" check (a plain boolean, not a try/catch) also covers
        /// an ordinary transient database exception -- not just an explicit <c>false</c> return (AC
        /// Cloud Mule review of issue #13, finding 1: an uncaught exception from
        /// <c>ShardDatabase.GetBiota</c>/<c>SaveBiota</c> used to bypass
        /// <see cref="SynchronouslyPersist"/>'s failure handling entirely and destroy the deposited
        /// item). Kept free of any live WorldObject/Player dependency so it can run in a unit test.
        /// </summary>
        internal static bool TryRunSynchronousPersist(Func<bool> persist, Action<Exception> onException)
        {
            try
            {
                return persist();
            }
            catch (Exception ex)
            {
                onException(ex);
                return false;
            }
        }

        private bool SynchronouslyPersist(WorldObject item) =>
            TryRunSynchronousPersist(
                () =>
                {
                    item.SaveBiotaToDatabase(false);
                    return DatabaseManager.Shard.BaseDatabase.SaveBiota(item.Biota, item.BiotaDatabaseLock);
                },
                ex => cloudCustodianLog.Error($"[CLOUD CUSTODIAN] Synchronous persist of 0x{item.Guid.Full:X8}:{item.Name} threw.", ex));

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
