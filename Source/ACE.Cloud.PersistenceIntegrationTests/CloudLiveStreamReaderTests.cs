using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// AC Cloud Mule issue #22's Red requirement: "Test public/private authorization scope, revoked
/// access, cross-tab reconnection, missed-event replay, and stale optimistic updates" against a real
/// <see cref="CloudLiveStreamReader"/> and MariaDB instance. Rows are inserted directly (rather than
/// through a real marketplace/custody flow) because this reader's authorization scoping must hold for
/// every future producer, not only the custody consumer this issue happens to wire up first.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudLiveStreamReaderTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static long _nextSourceSequenceNumber = 1;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _fixture = await CloudDatabaseFixture.StartAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _fixture.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await CloudBoundaryTestFixtureData.ResetAsync(_fixture.CloudConnectionString, ShardId);
    }

    [TestMethod]
    public async Task ReadAfterAsync_PublicEvent_IsReturnedToAnonymousViewer()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await InsertEventAsync(options, sequenceNumber: 1, isPublic: true, scopeOwnerId: null);

        await using var context = new CloudDbContext(options);
        var reader = new CloudLiveStreamReader(context);

        var events = await reader.ReadAfterAsync(CloudLiveStreamViewer.Anonymous(), afterSequenceNumber: 0, maxCount: 100);

        Assert.HasCount(1, events);
    }

    [TestMethod]
    public async Task ReadAfterAsync_PrivateEvent_IsHiddenFromAnUnrelatedViewer_ButVisibleToItsOwner()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var owner = Guid.NewGuid();
        await InsertEventAsync(options, sequenceNumber: 1, isPublic: false, scopeOwnerId: owner);

        await using var context = new CloudDbContext(options);
        var reader = new CloudLiveStreamReader(context);

        var unrelatedViewerEvents = await reader.ReadAfterAsync(
            CloudLiveStreamViewer.ForOwners([Guid.NewGuid()]), afterSequenceNumber: 0, maxCount: 100);
        Assert.IsEmpty(unrelatedViewerEvents);

        var ownerEvents = await reader.ReadAfterAsync(
            CloudLiveStreamViewer.ForOwners([owner]), afterSequenceNumber: 0, maxCount: 100);
        Assert.HasCount(1, ownerEvents);
    }

    [TestMethod]
    public async Task ReadAfterAsync_PrivateEvent_RevokedAccess_NoLongerReturnedOnTheNextRead()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var owner = Guid.NewGuid();
        await InsertEventAsync(options, sequenceNumber: 1, isPublic: false, scopeOwnerId: owner);

        await using var context = new CloudDbContext(options);
        var reader = new CloudLiveStreamReader(context);

        // Simulates a Sharing Grant revocation between two requests: the caller stops including the
        // owner in the authorized set it passes on the very next read.
        var beforeRevocation = await reader.ReadAfterAsync(CloudLiveStreamViewer.ForOwners([owner]), 0, 100);
        Assert.HasCount(1, beforeRevocation);

        var afterRevocation = await reader.ReadAfterAsync(CloudLiveStreamViewer.ForOwners([]), 0, 100);
        Assert.IsEmpty(afterRevocation);
    }

    [TestMethod]
    public async Task ReadAfterAsync_Admin_SeesEveryPrivateEvent_RegardlessOfOwner()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await InsertEventAsync(options, sequenceNumber: 1, isPublic: false, scopeOwnerId: Guid.NewGuid());
        await InsertEventAsync(options, sequenceNumber: 2, isPublic: false, scopeOwnerId: Guid.NewGuid());

        await using var context = new CloudDbContext(options);
        var reader = new CloudLiveStreamReader(context);

        var adminEvents = await reader.ReadAfterAsync(CloudLiveStreamViewer.ForAdmin(), afterSequenceNumber: 0, maxCount: 100);

        Assert.HasCount(2, adminEvents);
    }

    [TestMethod]
    public async Task ReadAfterAsync_CrossTabReconnection_ResumesExactlyAfterTheClientsLastSeenCursor()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var owner = Guid.NewGuid();
        var viewer = CloudLiveStreamViewer.ForOwners([owner]);

        await InsertEventAsync(options, sequenceNumber: 1, isPublic: false, scopeOwnerId: owner);

        await using var context = new CloudDbContext(options);
        var reader = new CloudLiveStreamReader(context);

        var firstTabEvents = await reader.ReadAfterAsync(viewer, afterSequenceNumber: 0, maxCount: 100);
        Assert.HasCount(1, firstTabEvents);
        var lastSeenCursor = firstTabEvents[^1].SequenceNumber;

        // A second event arrives while the first tab is disconnected.
        await InsertEventAsync(options, sequenceNumber: 2, isPublic: false, scopeOwnerId: owner);

        // Reconnecting (a second tab, or the same tab after a gap) with the last-seen cursor must
        // replay exactly what was missed -- not the whole stream and not nothing.
        var reconnectedEvents = await reader.ReadAfterAsync(viewer, lastSeenCursor, maxCount: 100);
        Assert.HasCount(1, reconnectedEvents);
        Assert.AreEqual(2L, reconnectedEvents[0].SequenceNumber);
    }

    [TestMethod]
    public async Task GetLatestSequenceNumberAsync_ReflectsInsertedEvents_AndIsZeroWhenEmpty()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using var emptyContext = new CloudDbContext(options);
        var emptyReader = new CloudLiveStreamReader(emptyContext);
        Assert.AreEqual(0, await emptyReader.GetLatestSequenceNumberAsync());

        await InsertEventAsync(options, sequenceNumber: 1, isPublic: true, scopeOwnerId: null);

        await using var context = new CloudDbContext(options);
        var reader = new CloudLiveStreamReader(context);
        Assert.AreEqual(1, await reader.GetLatestSequenceNumberAsync());
    }

    private static async Task InsertEventAsync(
        DbContextOptions<CloudDbContext> options, long sequenceNumber, bool isPublic, Guid? scopeOwnerId)
    {
        await using var context = new CloudDbContext(options);
        context.CloudLiveStreamEvents.Add(new CloudLiveStreamEvent(
            ShardId,
            sequenceNumber,
            isPublic,
            scopeOwnerId,
            "TestEvent",
            Guid.NewGuid(),
            Interlocked.Increment(ref _nextSourceSequenceNumber)));
        await context.SaveChangesAsync();
    }
}
