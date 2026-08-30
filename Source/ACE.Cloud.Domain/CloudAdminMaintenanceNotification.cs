namespace ACE.Cloud.Domain;

/// <summary>The kind of admin-webhook-worthy Global Cloud Maintenance fact (ADM-004).</summary>
public enum CloudAdminMaintenanceNotificationKind
{
    Entered,
    Exited,
}

/// <summary>
/// The admin webhook payload ADM-004 requires on every Global Cloud Maintenance entry/exit. Contains
/// no private account names, credentials, or DAT-derived content (AGENTS.md's privacy rule) -- only
/// the shard ID, the kind of transition, the administrator-supplied reason, and database time.
/// </summary>
public sealed record CloudAdminMaintenanceNotification(
    string ShardId,
    CloudAdminMaintenanceNotificationKind Kind,
    string? Reason,
    DateTime OccurredAtUtc);
