using System;
using System.Linq;

using log4net;

using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.WorldObjects;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// The in-game surface for AC Cloud Mule Withdrawal Tokens (WDR-001..WDR-008): a player-level
    /// redemption command, plus administrator controls for the Withdrawal Landblock allowlist and the
    /// shard-wide `withdraw anywhere` bypass (ADM-003). Location changes hot-apply immediately: there
    /// is no cached configuration to reapply -- <see cref="Player.HandleCloudWithdrawalRedeem"/> reads
    /// the current configuration fresh on every redemption attempt.
    /// </summary>
    public static class CloudWithdrawalCommands
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        [CommandHandler("withdraw", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 1,
            "Redeems a Withdrawal Token from your Cloud Mule web selection.",
            "<token>")]
        public static void HandleWithdraw(Session session, params string[] parameters)
        {
            session.Player.HandleCloudWithdrawalRedeem(parameters[0]);
        }

        [CommandHandler("withdrawlandblocks", AccessLevel.Admin, CommandHandlerFlag.None, 1,
            "Manages Withdrawal Landblocks and the withdraw-anywhere bypass.",
            "anywhere <on|off>\n" +
            "add <landblock hex> <name>\n" +
            "remove <landblock ID>\n" +
            "list\n" +
            "Example: @withdrawlandblocks add 0x123E Town Hall")]
        public static void HandleWithdrawLandblocks(Session session, params string[] parameters)
        {
            if (!ConfigManager.Config.CloudMule.Enabled)
            {
                CommandHandlerHelper.WriteOutputInfo(session, "AC Cloud Mule is disabled (CloudMule.Enabled = false in Config.js).");
                return;
            }

            var shardId = ConfigManager.Config.CloudMule.ShardId;
            if (string.IsNullOrWhiteSpace(shardId))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "AC Cloud Mule is enabled but CloudMule.ShardId is not configured.");
                return;
            }

            var subcommand = parameters[0].ToLowerInvariant();
            var rest = parameters.Skip(1).ToArray();

            switch (subcommand)
            {
                case "anywhere":
                    HandleAnywhere(session, shardId, rest);
                    break;

                case "add":
                    HandleAdd(session, shardId, rest);
                    break;

                case "remove":
                    HandleRemove(session, shardId, rest.FirstOrDefault());
                    break;

                case "list":
                    HandleList(session, shardId);
                    break;

                default:
                    CommandHandlerHelper.WriteOutputInfo(session, $"Unknown @withdrawlandblocks subcommand \"{subcommand}\". See @help withdrawlandblocks.");
                    break;
            }
        }

        private static void HandleAnywhere(Session session, string shardId, string[] args)
        {
            if (args.Length < 1 || !TryParseOnOff(args[0], out var enabled))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Usage: @withdrawlandblocks anywhere <on|off>");
                return;
            }

            RunAndReport(session, async boundary =>
            {
                var current = await boundary.GetCurrentAsync(shardId);
                return await boundary.SetWithdrawAnywhereEnabledAsync(shardId, enabled, current.Version.Value);
            }, $"Withdraw anywhere {(enabled ? "enabled" : "disabled")}.");
        }

        private static void HandleAdd(Session session, string shardId, string[] args)
        {
            if (args.Length < 2 || !TryParseLandblock(args[0], out var landblock))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Usage: @withdrawlandblocks add <landblock hex, e.g. 0x123E> <name>");
                return;
            }

            var name = string.Join(" ", args.Skip(1));

            RunAndReport(session, async boundary =>
            {
                var current = await boundary.GetCurrentAsync(shardId);
                return await boundary.AddNamedLandblockAsync(shardId, landblock, name, current.Version.Value);
            }, "Withdrawal Landblock added.");
        }

        private static void HandleRemove(Session session, string shardId, string landblockIdText)
        {
            if (!Guid.TryParse(landblockIdText, out var landblockId))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Usage: @withdrawlandblocks remove <landblock ID> (see @withdrawlandblocks list)");
                return;
            }

            RunAndReport(session, async boundary =>
            {
                var current = await boundary.GetCurrentAsync(shardId);
                return await boundary.RemoveNamedLandblockAsync(shardId, landblockId, current.Version.Value);
            }, "Withdrawal Landblock removed.");
        }

        private static void HandleList(Session session, string shardId)
        {
            try
            {
                var options = CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString());
                using var context = new CloudDbContext(options);
                var boundary = new CloudWithdrawalLocationConfigurationBoundary(context);

                var current = boundary.GetCurrentAsync(shardId).GetAwaiter().GetResult();

                var lines = new System.Collections.Generic.List<string>
                {
                    $"Withdrawal Landblock configuration (version {current.Version.Value}):",
                    $"  Withdraw anywhere: {(current.WithdrawAnywhereEnabled ? "enabled" : "disabled")}",
                    $"  Named landblocks ({current.NamedLandblocks.Count}):",
                };

                foreach (var landblock in current.NamedLandblocks)
                {
                    lines.Add($"    {landblock.Id}: 0x{landblock.Landblock:X4} \"{landblock.Name}\"");
                }

                CommandHandlerHelper.WriteOutputInfo(session, string.Join("\n", lines));
            }
            catch (Exception ex)
            {
                log.Error("AC Cloud Mule: @withdrawlandblocks list failed.", ex);
                CommandHandlerHelper.WriteOutputInfo(session, $"@withdrawlandblocks list failed: {ex.Message}");
            }
        }

        private static void RunAndReport(
            Session session,
            Func<CloudWithdrawalLocationConfigurationBoundary, System.Threading.Tasks.Task<CloudBoundaryOutcome<CloudWithdrawalLocationConfiguration>>> action,
            string successMessage)
        {
            try
            {
                var options = CloudDbContextOptionsFactory.Create(CloudCustodianManager.BuildCloudConnectionString());
                using var context = new CloudDbContext(options);
                var boundary = new CloudWithdrawalLocationConfigurationBoundary(context);

                var outcome = action(boundary).GetAwaiter().GetResult();

                if (outcome.Kind != CloudBoundaryOutcomeKind.Committed)
                {
                    CommandHandlerHelper.WriteOutputInfo(session, $"@withdrawlandblocks failed: {outcome.Reason}");
                    return;
                }

                CommandHandlerHelper.WriteOutputInfo(session, successMessage);
            }
            catch (Exception ex)
            {
                log.Error("AC Cloud Mule: @withdrawlandblocks command failed.", ex);
                CommandHandlerHelper.WriteOutputInfo(session, $"@withdrawlandblocks failed: {ex.Message}");
            }
        }

        private static bool TryParseLandblock(string text, out ushort landblock)
        {
            landblock = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[2..];
            }

            return ushort.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out landblock);
        }

        private static bool TryParseOnOff(string text, out bool enabled)
        {
            switch (text?.ToLowerInvariant())
            {
                case "on":
                case "true":
                case "enable":
                case "enabled":
                    enabled = true;
                    return true;

                case "off":
                case "false":
                case "disable":
                case "disabled":
                    enabled = false;
                    return true;

                default:
                    enabled = false;
                    return false;
            }
        }
    }
}
