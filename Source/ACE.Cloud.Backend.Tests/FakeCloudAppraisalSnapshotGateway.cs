using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudAppraisalSnapshotGateway"/> substitute for Backend endpoint tests.</summary>
internal sealed class FakeCloudAppraisalSnapshotGateway : ICloudAppraisalSnapshotGateway
{
    private readonly Dictionary<uint, CloudAppraisalRawItemSnapshot> _snapshotsByBiotaId = [];

    public void Seed(uint biotaId, CloudAppraisalRawItemSnapshot snapshot) => _snapshotsByBiotaId[biotaId] = snapshot;

    public Task<CloudAppraisalRawItemSnapshot?> TryGetAsync(uint biotaId, string shardId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshotsByBiotaId.TryGetValue(biotaId, out var snapshot) ? snapshot : null);
}
