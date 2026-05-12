using CycloneGames.Networking.Buffers;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkBufferPoolTests
    {
        [Test]
        public void Return_Ignores_Double_Dispose()
        {
            NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteInt(123);

            Assert.DoesNotThrow(() =>
            {
                buffer.Dispose();
                buffer.Dispose();
            });

            using NetworkBuffer next = NetworkBufferPool.Get();
            next.WriteInt(456);

            Assert.AreEqual(4, next.Position);
        }
    }
}
