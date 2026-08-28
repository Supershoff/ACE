using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ACE.Server.Managers;

namespace ACE.Server.Tests
{
    /// <summary>
    /// AC Cloud Mule issue #13: <see cref="CloudOwnerIdentity"/> must be a pure, deterministic
    /// mapping so repeated deposits for the same ACE account/biota always resolve to the same Cloud
    /// identity (ARCH-006, transaction rule 4), without depending on a live database.
    /// </summary>
    [TestClass]
    public class CloudOwnerIdentityTests
    {
        [TestMethod]
        public void ForAccount_IsDeterministic_ForTheSameShardAndAccount()
        {
            var first = CloudOwnerIdentity.ForAccount("us1", 42);
            var second = CloudOwnerIdentity.ForAccount("us1", 42);

            Assert.AreEqual(first, second);
        }

        [TestMethod]
        public void ForAccount_DiffersAcrossAccounts()
        {
            var accountOne = CloudOwnerIdentity.ForAccount("us1", 1);
            var accountTwo = CloudOwnerIdentity.ForAccount("us1", 2);

            Assert.AreNotEqual(accountOne, accountTwo);
        }

        [TestMethod]
        public void ForAccount_DiffersAcrossShards()
        {
            var shardOne = CloudOwnerIdentity.ForAccount("us1", 42);
            var shardTwo = CloudOwnerIdentity.ForAccount("us2", 42);

            Assert.AreNotEqual(shardOne, shardTwo);
        }

        [TestMethod]
        public void ForAccount_NeverReturnsEmpty()
        {
            Assert.AreNotEqual(Guid.Empty, CloudOwnerIdentity.ForAccount("us1", 0));
        }

        [TestMethod]
        public void DepositIdempotencyKey_IsDeterministic_ForTheSameShardAndBiota()
        {
            var first = CloudOwnerIdentity.DepositIdempotencyKey("us1", 0x80000123);
            var second = CloudOwnerIdentity.DepositIdempotencyKey("us1", 0x80000123);

            Assert.AreEqual(first, second);
        }

        [TestMethod]
        public void DepositIdempotencyKey_DiffersAcrossBiotas()
        {
            var biotaOne = CloudOwnerIdentity.DepositIdempotencyKey("us1", 0x80000123);
            var biotaTwo = CloudOwnerIdentity.DepositIdempotencyKey("us1", 0x80000124);

            Assert.AreNotEqual(biotaOne, biotaTwo);
        }

        [TestMethod]
        public void DepositIdempotencyKey_DiffersFromForAccount_EvenWithOverlappingInputs()
        {
            // Guards against an implementation detail (e.g. reusing the same seed format) accidentally
            // making an owner ID collide with an idempotency key for the same shard.
            var ownerId = CloudOwnerIdentity.ForAccount("us1", 123);
            var idempotencyKey = CloudOwnerIdentity.DepositIdempotencyKey("us1", 123);

            Assert.AreNotEqual(ownerId, idempotencyKey);
        }

        [TestMethod]
        public void DepositIdempotencyKey_NeverReturnsEmpty()
        {
            Assert.AreNotEqual(Guid.Empty, CloudOwnerIdentity.DepositIdempotencyKey("us1", 1));
        }
    }
}
