using CycloneGames.Networking.Security;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class RateLimiterTests
    {
        [Test]
        public void TryConsume_Uses_Token_Bucket_Refill()
        {
            var limiter = new RateLimiter(maxMessagesPerSecond: 2, maxBytesPerSecond: 100, burstLimit: 0);

            Assert.IsTrue(limiter.TryConsume(1, 10, 0f));
            Assert.IsTrue(limiter.TryConsume(1, 10, 0f));
            Assert.IsFalse(limiter.TryConsume(1, 10, 0f));

            Assert.IsTrue(limiter.TryConsume(1, 10, 0.5f));
            Assert.IsFalse(limiter.TryConsume(1, 10, 0.5f));
        }

        [Test]
        public void TryConsume_Rejects_Byte_Budget_Overflow()
        {
            var limiter = new RateLimiter(maxMessagesPerSecond: 10, maxBytesPerSecond: 20, burstLimit: 0);

            Assert.IsTrue(limiter.TryConsume(1, 15, 0f));
            Assert.IsFalse(limiter.TryConsume(1, 6, 0f));
            Assert.IsTrue(limiter.TryConsume(1, 6, 0.3f));
        }

        [Test]
        public void TryConsume_Rejects_Invalid_Time_And_Connection_Without_Growing_State()
        {
            var limiter = new RateLimiter();

            Assert.IsFalse(limiter.TryConsume(0, 1, 0d));
            Assert.IsFalse(limiter.TryConsume(1, 1, double.NaN));
            Assert.IsFalse(limiter.TryConsume(1, 1, double.PositiveInfinity));
            Assert.AreEqual(0, limiter.TrackedConnectionCount);
        }

        [Test]
        public void TryConsume_Rejects_New_Connection_When_Capacity_Is_Exhausted()
        {
            var limiter = new RateLimiter(maxTrackedConnections: 1, idleTimeoutSeconds: 10d);

            Assert.IsTrue(limiter.TryConsume(1, 1, 0d));
            Assert.IsFalse(limiter.TryConsume(2, 1, 0d));
            Assert.AreEqual(1, limiter.TrackedConnectionCount);

            Assert.AreEqual(1, limiter.PruneExpired(10d));
            Assert.IsTrue(limiter.TryConsume(2, 1, 10d));
        }

        [Test]
        public void TryConsume_Rejects_Clock_Regression_Without_Refilling_Twice()
        {
            var limiter = new RateLimiter(maxMessagesPerSecond: 1, maxBytesPerSecond: 100, burstLimit: 0);

            Assert.IsTrue(limiter.TryConsume(1, 1, 10d));
            Assert.IsFalse(limiter.TryConsume(1, 1, 9d));
            Assert.IsFalse(limiter.TryConsume(1, 1, 10d));
            Assert.IsTrue(limiter.TryConsume(1, 1, 11d));
        }

        [Test]
        public void Clear_Resets_Tracked_State_And_Capacity()
        {
            var limiter = new RateLimiter(
                maxMessagesPerSecond: 1,
                maxBytesPerSecond: 1,
                burstLimit: 0,
                maxTrackedConnections: 1);

            Assert.IsTrue(limiter.TryConsume(1, 1, 0d));
            Assert.IsFalse(limiter.TryConsume(2, 1, 0d));

            limiter.Clear();

            Assert.AreEqual(0, limiter.TrackedConnectionCount);
            Assert.IsTrue(limiter.TryConsume(2, 1, 0d));
        }
    }
}
