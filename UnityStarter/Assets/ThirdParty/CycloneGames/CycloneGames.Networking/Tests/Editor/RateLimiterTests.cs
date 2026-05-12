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
    }
}
