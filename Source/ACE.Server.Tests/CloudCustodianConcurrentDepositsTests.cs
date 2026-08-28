using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ACE.Server.WorldObjects;

namespace ACE.Server.Tests
{
    /// <summary>
    /// AC Cloud Mule review of issue #13, finding 3: the Cloud Custodian deposit handler used to await
    /// each submitted row's Cloud custody call one at a time on the single ACE world-tick thread, so a
    /// submission of N rows stalled the entire server for the cumulative round-trip time of N
    /// sequential Cloud-DB transactions. Exercised directly against
    /// <see cref="Player.RunConcurrentlyAsync{T}"/> (no live WorldObject/database needed) so the
    /// concurrent-vs-sequential regression is covered without requiring ACE's world/database
    /// bootstrap.
    /// </summary>
    [TestClass]
    public class CloudCustodianConcurrentDepositsTests
    {
        [TestMethod]
        public async Task RunConcurrentlyAsync_MultipleDelayedOperations_RunsThemConcurrentlyNotSequentially()
        {
            const int delayMs = 200;
            const int operationCount = 4;

            var operations = Enumerable.Range(0, operationCount)
                .Select(i => (Func<Task<int>>)(async () =>
                {
                    await Task.Delay(delayMs);
                    return i;
                }))
                .ToList();

            var stopwatch = Stopwatch.StartNew();
            var results = await Player.RunConcurrentlyAsync(operations);
            stopwatch.Stop();

            CollectionAssert.AreEquivalent(new List<int> { 0, 1, 2, 3 }, results.ToList());

            // Running these operations one at a time (the pre-fix behavior) would take at least
            // operationCount * delayMs (800ms). A generous ceiling of less than double a single
            // operation's delay keeps this robust against ordinary CI scheduling jitter while still
            // failing if the rows are run sequentially instead of concurrently.
            Assert.IsTrue(
                stopwatch.ElapsedMilliseconds < delayMs * 2,
                $"Expected concurrent execution to complete in well under {operationCount * delayMs}ms (sequential), took {stopwatch.ElapsedMilliseconds}ms.");
        }

        [TestMethod]
        public async Task RunConcurrentlyAsync_PreservesEachOperationsOwnResultInOrder()
        {
            var operations = new List<Func<Task<string>>>
            {
                () => Task.FromResult("first"),
                () => Task.FromResult("second"),
                () => Task.FromResult("third"),
            };

            var results = await Player.RunConcurrentlyAsync(operations);

            CollectionAssert.AreEqual(new List<string> { "first", "second", "third" }, results.ToList());
        }

        [TestMethod]
        public async Task RunConcurrentlyAsync_NoOperations_ReturnsAnEmptyList()
        {
            var results = await Player.RunConcurrentlyAsync(new List<Func<Task<int>>>());

            Assert.AreEqual(0, results.Count);
        }
    }
}
