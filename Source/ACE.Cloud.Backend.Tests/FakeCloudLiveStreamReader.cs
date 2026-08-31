using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudLiveStreamReader"/> substitute.</summary>
internal sealed class FakeCloudLiveStreamReader : ICloudLiveStreamReader
{
    public List<CloudLiveStreamEvent> Events { get; } = [];

    public Task<IReadOnlyList<CloudLiveStreamEvent>> ReadAfterAsync(
        CloudLiveStreamViewer viewer, long afterSequenceNumber, int maxCount, CancellationToken cancellationToken = default)
    {
        var authorizedOwnerIds = viewer.AuthorizedOwnerIds;
        IReadOnlyList<CloudLiveStreamEvent> result = Events
            .Where(evt => evt.SequenceNumber > afterSequenceNumber)
            .Where(evt => viewer.IsAdmin || evt.IsPublic || (evt.ScopeOwnerId is { } ownerId && authorizedOwnerIds.Contains(ownerId)))
            .OrderBy(evt => evt.SequenceNumber)
            .Take(maxCount)
            .ToList();
        return Task.FromResult(result);
    }
}
