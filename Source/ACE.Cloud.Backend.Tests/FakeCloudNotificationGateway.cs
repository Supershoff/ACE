using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudNotificationReader"/>/<see cref="ICloudNotificationWriter"/> substitute.</summary>
internal sealed class FakeCloudNotificationGateway : ICloudNotificationReader, ICloudNotificationWriter
{
    private sealed record Row(Guid OwnerId, CloudNotificationSnapshot Snapshot);

    private readonly List<Row> _rows = [];

    public void Seed(Guid ownerId, CloudNotificationSnapshot snapshot) => _rows.Add(new Row(ownerId, snapshot));

    public Task<IReadOnlyList<CloudNotificationSnapshot>> ListAsync(
        string shardId, CloudLiveStreamViewer viewer, CancellationToken cancellationToken = default)
    {
        var authorizedOwnerIds = viewer.AuthorizedOwnerIds;
        IReadOnlyList<CloudNotificationSnapshot> result = _rows
            .Where(row => authorizedOwnerIds.Contains(row.OwnerId))
            .OrderByDescending(row => row.Snapshot.LastOccurredAtUtc)
            .Select(row => row.Snapshot)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<CloudNotificationUnreadSummary> GetUnreadSummaryAsync(
        string shardId, CloudLiveStreamViewer viewer, CancellationToken cancellationToken = default)
    {
        var authorizedOwnerIds = viewer.AuthorizedOwnerIds;
        var unreadCount = _rows.Count(row => authorizedOwnerIds.Contains(row.OwnerId) && !row.Snapshot.IsRead);
        return Task.FromResult(new CloudNotificationUnreadSummary(unreadCount));
    }

    public Task<bool> TryMarkReadAsync(
        string shardId, CloudLiveStreamViewer viewer, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var index = _rows.FindIndex(row => row.Snapshot.Id == notificationId);
        if (index < 0 || !viewer.AuthorizedOwnerIds.Contains(_rows[index].OwnerId))
        {
            return Task.FromResult(false);
        }

        var current = _rows[index];
        _rows[index] = current with
        {
            Snapshot = current.Snapshot with { IsRead = true },
        };
        return Task.FromResult(true);
    }
}
