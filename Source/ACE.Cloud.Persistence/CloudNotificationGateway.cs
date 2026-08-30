using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Interface-extracted (mirroring <see cref="ICloudInventoryQueryReader"/>) so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake instead of a real MariaDB-backed
/// <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudNotificationReader
{
    Task<IReadOnlyList<CloudNotificationSnapshot>> ListAsync(
        string shardId, CloudLiveStreamViewer viewer, CancellationToken cancellationToken = default);

    Task<CloudNotificationUnreadSummary> GetUnreadSummaryAsync(
        string shardId, CloudLiveStreamViewer viewer, CancellationToken cancellationToken = default);
}

/// <summary>Interface-extracted for the same reason as <see cref="ICloudNotificationReader"/>.</summary>
public interface ICloudNotificationWriter
{
    Task<bool> TryMarkReadAsync(
        string shardId, CloudLiveStreamViewer viewer, Guid notificationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The Notification Center's read/mutate surface (EVT-003): list, unread badge count, and mark-read.
/// A viewer only ever sees/marks-read their own notifications -- there is no admin "every owner's
/// notifications" view (unlike the Activity Ledger, a notification is inherently personal, not an
/// audit record), so this type deliberately does not special-case <see cref="CloudLiveStreamViewer.IsAdmin"/>
/// the way <see cref="CloudActivityLedgerQueryReader"/> does.
/// </summary>
public sealed class CloudNotificationGateway : ICloudNotificationReader, ICloudNotificationWriter
{
    private readonly CloudDbContext _context;

    public CloudNotificationGateway(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<CloudNotificationSnapshot>> ListAsync(
        string shardId, CloudLiveStreamViewer viewer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A notification query requires a Cloud Shard ID.", nameof(shardId));
        }

        ArgumentNullException.ThrowIfNull(viewer);

        var ownerIds = viewer.AuthorizedOwnerIds;
        if (ownerIds.Count == 0)
        {
            return [];
        }

        var rows = await _context.CloudNotifications.AsNoTracking()
            .Where(row => row.ShardId == shardId && ownerIds.Contains(row.OwnerId))
            .OrderByDescending(row => row.LastOccurredAtUtc)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(ToSnapshot);
    }

    public async Task<CloudNotificationUnreadSummary> GetUnreadSummaryAsync(
        string shardId, CloudLiveStreamViewer viewer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A notification query requires a Cloud Shard ID.", nameof(shardId));
        }

        ArgumentNullException.ThrowIfNull(viewer);

        var ownerIds = viewer.AuthorizedOwnerIds;
        if (ownerIds.Count == 0)
        {
            return new CloudNotificationUnreadSummary(0);
        }

        var unreadCount = await _context.CloudNotifications.AsNoTracking()
            .CountAsync(row => row.ShardId == shardId && ownerIds.Contains(row.OwnerId) && !row.IsRead, cancellationToken);

        return new CloudNotificationUnreadSummary(unreadCount);
    }

    /// <summary>
    /// Marks one notification read, but only when <paramref name="viewer"/> is currently authorized
    /// for its owner (a revoked Sharing Grant/unlinked account must not be able to mark-read a
    /// notification it can no longer see -- "revoked permissions" from issue #34's Red section).
    /// Returns false for a notification that does not exist or is not visible to this viewer, which
    /// the caller reports identically to "not found" (never distinguishing the two, matching the
    /// existing item-visibility precedent in <c>CloudInventoryEndpoints</c>).
    /// </summary>
    public async Task<bool> TryMarkReadAsync(
        string shardId, CloudLiveStreamViewer viewer, Guid notificationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Marking a notification read requires a Cloud Shard ID.", nameof(shardId));
        }

        ArgumentNullException.ThrowIfNull(viewer);

        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var notification = await _context.CloudNotifications
            .SingleOrDefaultAsync(row => row.ShardId == shardId && row.Id == notificationId, cancellationToken);

        if (notification is null || !viewer.AuthorizedOwnerIds.Contains(notification.OwnerId))
        {
            return false;
        }

        notification.MarkRead(await GetDatabaseUtcNowAsync(cancellationToken));
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static CloudNotificationSnapshot ToSnapshot(CloudNotification row) => new(
        row.Id, row.Kind, row.Destination, row.OccurrenceCount, row.IsRead, row.FirstOccurredAtUtc, row.LastOccurredAtUtc);

    private async Task<DateTime> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT UTC_TIMESTAMP(6);";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return DateTime.SpecifyKind(Convert.ToDateTime(result), DateTimeKind.Utc);
    }
}
