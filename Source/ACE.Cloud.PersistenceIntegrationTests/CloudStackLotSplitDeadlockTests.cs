using ACE.Cloud.Persistence;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green test for the .claude-review.md P1 finding on PR #146 (issue #33): the new
/// HTTP-reachable <see cref="CloudStackLotTransactionAuthority.SplitOwnLotAsync"/> locks a Cloud
/// Stack Lot's backing <c>CloudCustodyRecord</c> before the lot itself, while
/// <c>CloudCustodyBoundary.TryReserveForWithdrawalOnceAsync</c> (the stack-lot branch of
/// <c>ReserveForWithdrawalAsync</c>, reachable from <c>POST /withdrawals</c>) locks the lot before
/// its custody record -- the opposite order. Two browser tabs racing a split and a reservation
/// against the same lot can therefore deadlock (MariaDB error 1213), and unlike every other
/// mutating boundary method, <see cref="CloudStackLotTransactionAuthority"/> never ran its
/// transactions through <see cref="CloudBoundaryRetry"/>, so the loser's raw provider exception
/// escaped as an unhandled 500 instead of being retried.
///
/// This test forces a genuine AB-BA deadlock between the real
/// <see cref="CloudStackLotTransactionAuthority.SplitOwnLotAsync"/> production call (Record then
/// Lot) and a hand-rolled raw-SQL transaction that reproduces
/// <c>TryReserveForWithdrawalOnceAsync</c>'s exact lock order and lock statements (Lot then
/// Record, matching <c>CloudCustodyBoundary.cs</c>'s own <c>LockStackLotAsync</c>/
/// <c>LockCustodyRecordAsync</c> SQL text). The split side's test-only
/// <c>testOnlyLockInterleaveHook</c> pins the interleaving deterministically -- MariaDB's deadlock
/// detector always resolves the cycle by rolling back whichever side's lock request closes it, so
/// without pinning the order, a purely timing-based race (Task.Delay on the reservation side alone)
/// reliably deadlocks but just as reliably makes the *reservation* side the closer/victim, never
/// exercising the split side's (fixed) retry path. Pinning both edges of the cycle -- reservation
/// blocked waiting on the record, *then* split requests the lot -- makes the split side the closer
/// on every run, exactly reproducing the two-tabs scenario from the review finding.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudStackLotSplitDeadlockTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 900_000;

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
    public async Task SplitOwnLotAsync_DeadlockedAgainstAReservationsLockOrder_RetriesInsteadOfThrowing()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        Guid custodyRecordId;
        Guid lotId;
        int lotVersion;
        await using (var depositContext = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(depositContext);
            var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 10, Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind, depositOutcome.Reason);
            custodyRecordId = depositOutcome.Value!.CustodyRecord.Id;
            lotId = depositOutcome.Value!.Lot.Id;
            lotVersion = depositOutcome.Value!.Lot.Version;
        }

        var custodyRecordLockedBySplit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Reservation side: locks the lot immediately (uncontended), then -- once the split side has
        // locked the record -- attempts to lock the record too. That second attempt blocks (split
        // holds the record) without yet forming a cycle, since split has not asked for the lot yet.
        // Wrapped in the same retry helper the real ReserveForWithdrawalAsync already uses, matching
        // production fidelity: if MariaDB ever picked this side as the victim instead, it would
        // recover transparently rather than making the test flaky.
        var reservationLockOrderTask = CloudBoundaryRetry.ExecuteWithDeadlockRetryAsync(async () =>
        {
            await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await LockRowAsync(connection, transaction, "CloudStackLot", lotId);

            await custodyRecordLockedBySplit.Task;
            await LockRowAsync(connection, transaction, "CloudCustodyRecord", custodyRecordId);

            await transaction.CommitAsync();
            return true;
        });

        // Split side: the real production call. Its test-only hook fires right after the record lock
        // is acquired and before the lot lock is requested -- it signals the reservation side to make
        // its (now-blocking) record request, waits long enough for that request to actually reach the
        // server and start waiting, and only then lets the split continue on to request the lot. That
        // ordering guarantees the split's own lot request is the one that closes the cycle, so it is
        // the side MariaDB's deadlock detector rolls back.
        var splitTask = Task.Run(async () =>
        {
            await using var splitContext = new CloudDbContext(options);
            var authority = new CloudStackLotTransactionAuthority(splitContext);
            return await authority.SplitOwnLotAsync(
                lotId,
                lotVersion,
                ownerId,
                quantityToSplit: 4,
                testOnlyLockInterleaveHook: async () =>
                {
                    custodyRecordLockedBySplit.TrySetResult();
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                },
                cancellationToken: default);
        });

        var splitOutcome = await splitTask;
        await reservationLockOrderTask;

        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            splitOutcome.Kind,
            "A deadlock loss on the newly HTTP-reachable split path must be retried transparently " +
            "(CloudBoundaryRetry), not surfaced as an unhandled provider exception.");

        await using var verifyContext = new CloudDbContext(options);
        var lots = verifyContext.CloudStackLots.Where(l => l.CustodyRecordId == custodyRecordId).ToList();
        Assert.AreEqual(10, lots.Sum(l => l.Quantity), "Conservation must hold once the retried split commits.");
    }

    private static async Task LockRowAsync(MySqlConnection connection, MySqlTransaction transaction, string table, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM {table} WHERE Id = @id FOR UPDATE;";
        command.Parameters.AddWithValue("@id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
    }

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
