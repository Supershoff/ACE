using System;

using log4net;

using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Common;

namespace ACE.Server.Managers
{
    /// <summary>
    /// AC Cloud Mule issue #17 (AUTH-003, VAULT-001, VAULT-004, VAULT-005, ARCH-007): the ACE-side
    /// entry point authoritative character/allegiance seams call to publish identity/allegiance
    /// events into the Custody Outbox and to enforce the nonempty-vault monarch deletion guard.
    /// Every method is a no-op (or, for the deletion guard, fails safe by blocking) unless
    /// <c>CloudMule.Enabled</c> is configured, matching <see cref="CloudCustodianManager"/>'s
    /// established opt-in pattern. Calls here are synchronous/blocking (mirroring
    /// <see cref="Player.TryRunSynchronousPersist"/>'s established pattern for other Cloud Mule
    /// seams): callers are ACE's single-threaded command/handler code that needs a definite answer
    /// (the deletion guard) or a durably published event before continuing, not a background worker.
    /// </summary>
    public static class CloudIdentityEventManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// VAULT-005: refuses deletion of a monarch character whose Allegiance Vault is nonempty.
        /// <paramref name="isMonarch"/> is the caller's own live ACE allegiance-tree fact (whether
        /// this character currently leads an allegiance); everything else Cloud-side is looked up
        /// here. Fails safe by blocking (never guessing "allowed") if AC Cloud Mule is enabled but
        /// its Allegiance Vault status cannot currently be verified, since silently allowing the
        /// deletion is exactly the unrecoverable outcome this guard exists to prevent.
        /// </summary>
        public static CloudMonarchDeletionDecision CheckMonarchDeletion(uint characterId, bool isMonarch)
        {
            if (!ConfigManager.Config.CloudMule.Enabled || !isMonarch)
            {
                return CloudMonarchDeletionDecision.Allow();
            }

            var shardId = ConfigManager.Config.CloudMule.ShardId;
            if (string.IsNullOrWhiteSpace(shardId))
            {
                log.Error("AC Cloud Mule: CloudMule.ShardId is not configured; refusing this monarch deletion as a fail-safe (VAULT-005).");
                return CloudMonarchDeletionDecision.Block(
                    "AC Cloud Mule is misconfigured (missing ShardId); this monarch's Allegiance Vault status cannot be verified.");
            }

            try
            {
                var options = CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString());
                using var context = new CloudDbContext(options);
                var vaultGateway = new CloudAllegianceVaultGateway(context);

                var vaultIsEmpty = vaultGateway.GetIsEmptyAsync(shardId, characterId).GetAwaiter().GetResult();
                return CloudMonarchDeletionGuard.Evaluate(isMonarch, vaultIsEmpty);
            }
            catch (Exception ex)
            {
                log.Error($"AC Cloud Mule: failed to check Allegiance Vault status for monarch 0x{characterId:X8}; refusing deletion as a fail-safe (VAULT-005).", ex);
                return CloudMonarchDeletionDecision.Block(
                    "AC Cloud Mule could not verify this monarch's Allegiance Vault status; deletion is refused until Cloud services are available.");
            }
        }

        /// <summary>AUTH-003: publishes a character rename event.</summary>
        public static void PublishCharacterRenamed(uint characterId, uint accountId, string characterName, int totalLogins) =>
            PublishCharacterEvent(CloudIdentityEventType.CharacterRenamed, characterId, accountId, characterName, totalLogins);

        /// <summary>AUTH-003: publishes a character deletion event.</summary>
        public static void PublishCharacterDeleted(uint characterId, uint accountId, string characterName, int totalLogins) =>
            PublishCharacterEvent(CloudIdentityEventType.CharacterDeleted, characterId, accountId, characterName, totalLogins);

        /// <summary>
        /// VAULT-001: publishes an allegiance swear/break/monarch-change event. Issue #39's oath-first
        /// fix: callers must pass the character's own authoritative account/name/login snapshot
        /// (readily available from the in-memory <c>Character</c> at every allegiance call site) so the
        /// resulting projection is account-associated even when this is the character's first-ever
        /// identity/allegiance event in a fresh Cloud database.
        /// </summary>
        public static void PublishAllegianceEvent(
            uint characterId,
            CloudIdentityEventType eventType,
            uint? monarchId,
            uint? priorMonarchId,
            uint accountId,
            string characterName,
            int totalLogins)
        {
            if (!TryGetShardId(out var shardId))
            {
                return;
            }

            try
            {
                var options = CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString());
                using var context = new CloudDbContext(options);
                var identityGateway = new CloudIdentityEventGateway(context);
                identityGateway.PublishAllegianceEventAsync(
                        shardId, eventType, characterId, monarchId, priorMonarchId, accountId, characterName, totalLogins, Guid.NewGuid())
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                log.Error($"AC Cloud Mule: failed to publish {eventType} for character 0x{characterId:X8}.", ex);
            }
        }

        /// <summary>
        /// Issue #39's self-heal fix: publishes an authoritative character-observed/login snapshot at
        /// every successful world login, carrying this character's current account/name/login/monarch
        /// snapshot. Unlike <see cref="PublishAllegianceEvent"/>, this is never a swear/break/monarch-
        /// change -- it is an idempotent per-login observation, published whether or not anything about
        /// the character's allegiance actually changed this session. It exists specifically so a
        /// character whose only prior Cloud identity/allegiance event predates the oath-first fix (and
        /// so left a projection row with a null account/name association, or an otherwise stale monarch
        /// pointer) is repaired the next time they log in, since ordinary login otherwise publishes no
        /// identity/allegiance event at all.
        /// </summary>
        public static void PublishCharacterLoginObserved(uint characterId, uint accountId, string characterName, int totalLogins, uint? monarchId)
        {
            if (!TryGetShardId(out var shardId))
            {
                return;
            }

            try
            {
                var options = CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString());
                using var context = new CloudDbContext(options);
                var identityGateway = new CloudIdentityEventGateway(context);
                identityGateway.PublishCharacterLoginObservedEventAsync(shardId, characterId, monarchId, accountId, characterName, totalLogins, Guid.NewGuid())
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                log.Error($"AC Cloud Mule: failed to publish CharacterLoginObserved for character 0x{characterId:X8}.", ex);
            }
        }

        /// <summary>
        /// VAULT-004: absorbs every item from <paramref name="oldMonarchCharacterId"/>'s Allegiance
        /// Vault into <paramref name="newMonarchCharacterId"/>'s, called when a monarch swears into
        /// another allegiance.
        /// </summary>
        public static void AbsorbVault(uint oldMonarchCharacterId, uint newMonarchCharacterId)
        {
            if (!TryGetShardId(out var shardId))
            {
                return;
            }

            try
            {
                var options = CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString());
                using var context = new CloudDbContext(options);
                var vaultGateway = new CloudAllegianceVaultGateway(context);

                var outcome = vaultGateway.AbsorbAsync(shardId, oldMonarchCharacterId, newMonarchCharacterId)
                    .GetAwaiter().GetResult();
                if (outcome.Kind != CloudBoundaryOutcomeKind.Committed)
                {
                    log.Error($"AC Cloud Mule: Vault Absorption from monarch 0x{oldMonarchCharacterId:X8} to 0x{newMonarchCharacterId:X8} failed: {outcome.Reason}");
                }
            }
            catch (Exception ex)
            {
                log.Error($"AC Cloud Mule: failed to absorb the Allegiance Vault from monarch 0x{oldMonarchCharacterId:X8} into 0x{newMonarchCharacterId:X8}.", ex);
            }
        }

        /// <summary>
        /// VAULT-005: scans for out-of-band monarch deletions once (intended to run at ACE startup,
        /// mirroring <see cref="CloudCustodianManager.Initialize"/>'s established opt-in bootstrap
        /// pattern): a vault whose monarch no longer exists despite ACE's own guard never having
        /// blocked it. Logs any newly found case for audited administrator recovery; never guesses a
        /// successor. Safe to call repeatedly -- an already-diagnosed vault is never re-diagnosed.
        /// </summary>
        public static void RunStartupIntegrityCheck()
        {
            if (!TryGetShardId(out var shardId))
            {
                return;
            }

            try
            {
                var options = CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString());
                using var context = new CloudDbContext(options);
                var vaultGateway = new CloudAllegianceVaultGateway(context);

                var diagnostics = vaultGateway.DetectOutOfBandMonarchVaultOrphansAsync(shardId).GetAwaiter().GetResult();
                foreach (var diagnostic in diagnostics)
                {
                    log.Warn($"AC Cloud Mule: {diagnostic.Reason}");
                }
            }
            catch (Exception ex)
            {
                log.Error("AC Cloud Mule: failed to run the monarch Allegiance Vault out-of-band deletion integrity check.", ex);
            }
        }

        private static void PublishCharacterEvent(
            CloudIdentityEventType eventType, uint characterId, uint accountId, string characterName, int totalLogins)
        {
            if (!TryGetShardId(out var shardId))
            {
                return;
            }

            try
            {
                var options = CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString());
                using var context = new CloudDbContext(options);
                var identityGateway = new CloudIdentityEventGateway(context);
                identityGateway.PublishCharacterIdentityEventAsync(shardId, eventType, characterId, accountId, characterName, totalLogins, Guid.NewGuid())
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                log.Error($"AC Cloud Mule: failed to publish {eventType} for character 0x{characterId:X8}.", ex);
            }
        }

        private static bool TryGetShardId(out string shardId)
        {
            shardId = ConfigManager.Config.CloudMule.ShardId;

            if (!ConfigManager.Config.CloudMule.Enabled)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(shardId))
            {
                log.Error("AC Cloud Mule: CloudMule.ShardId is not configured; skipping this identity/allegiance event.");
                return false;
            }

            return true;
        }
    }
}
