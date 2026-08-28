using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ACE.Server.WorldObjects;

namespace ACE.Server.Tests
{
    /// <summary>
    /// AC Cloud Mule review of issue #13, finding 1: an uncaught exception from
    /// <c>ShardDatabase.GetBiota</c>/<c>SaveBiota</c> (for example a transient database error) used
    /// to propagate straight out of <c>Player_CloudCustodian.SynchronouslyPersist</c> instead of
    /// being reported and handled as an ordinary failure. Exercised directly against
    /// <see cref="Player.TryRunSynchronousPersist"/> (no live WorldObject/database needed) so the
    /// exception-to-failure mapping is covered without requiring ACE's world/database bootstrap.
    /// </summary>
    [TestClass]
    public class CloudCustodianSynchronousPersistTests
    {
        [TestMethod]
        public void TryRunSynchronousPersist_PersistThrows_ReturnsFalseInsteadOfPropagating()
        {
            Exception observed = null;

            var result = Player.TryRunSynchronousPersist(
                () => throw new InvalidOperationException("simulated GetBiota/SaveBiota failure"),
                ex => observed = ex);

            Assert.IsFalse(result, "An exception from the persist step must be treated as a failure, not propagate uncaught.");
            Assert.IsInstanceOfType(observed, typeof(InvalidOperationException));
        }

        [TestMethod]
        public void TryRunSynchronousPersist_PersistSucceeds_ReturnsTrueAndReportsNoException()
        {
            var observedException = false;

            var result = Player.TryRunSynchronousPersist(() => true, ex => observedException = true);

            Assert.IsTrue(result);
            Assert.IsFalse(observedException);
        }

        [TestMethod]
        public void TryRunSynchronousPersist_PersistReturnsFalseWithoutThrowing_ReturnsFalseAndReportsNoException()
        {
            var observedException = false;

            var result = Player.TryRunSynchronousPersist(() => false, ex => observedException = true);

            Assert.IsFalse(result);
            Assert.IsFalse(observedException);
        }
    }
}
