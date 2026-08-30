namespace ACE.Cloud.Domain;

/// <summary>
/// The admin webhook seam ADM-004 requires on Global Cloud Maintenance entry/exit. Deliberately
/// declared in this pure-domain assembly (no HTTP dependency here) so
/// <c>ACE.Cloud.Persistence</c>'s <c>CloudGlobalMaintenanceBoundary</c> can depend on it without
/// depending on any particular transport; a real HTTP-backed implementation lives in
/// <c>ACE.Cloud.Hosting</c>, matching <c>ICloudWorldBoundaryHealthProbe</c>'s established shape.
/// Notification is always best-effort: a failed or slow webhook call must never roll back or block
/// the maintenance transition it is reporting.
/// </summary>
public interface IAdminMaintenanceNotifier
{
    Task NotifyAsync(CloudAdminMaintenanceNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>The default notifier when no admin webhook is configured: does nothing.</summary>
public sealed class NoOpAdminMaintenanceNotifier : IAdminMaintenanceNotifier
{
    public static NoOpAdminMaintenanceNotifier Instance { get; } = new();

    public Task NotifyAsync(CloudAdminMaintenanceNotification notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
