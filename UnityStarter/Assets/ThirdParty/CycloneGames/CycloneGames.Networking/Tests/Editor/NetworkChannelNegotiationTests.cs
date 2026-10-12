using CycloneGames.Networking.Transports;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkChannelNegotiationTests
    {
        [Test]
        public void ExactMatch_ResolvesToRequested()
        {
            bool ok = NetworkChannelNegotiation.TryResolve(
                NetworkChannel.Reliable, NetworkChannelFlags.All, out NetworkChannel resolved);

            Assert.IsTrue(ok);
            Assert.AreEqual(NetworkChannel.Reliable, resolved);
        }

        [Test]
        public void ReliableUnordered_DegradesToReliable()
        {
            bool ok = NetworkChannelNegotiation.TryResolve(
                NetworkChannel.ReliableUnordered,
                NetworkChannelFlags.Reliable | NetworkChannelFlags.Unreliable,
                out NetworkChannel resolved);

            Assert.IsTrue(ok);
            Assert.AreEqual(NetworkChannel.Reliable, resolved);
        }

        [Test]
        public void UnreliableSequenced_DegradesToUnreliable()
        {
            bool ok = NetworkChannelNegotiation.TryResolve(
                NetworkChannel.UnreliableSequenced,
                NetworkChannelFlags.Reliable | NetworkChannelFlags.Unreliable,
                out NetworkChannel resolved);

            Assert.IsTrue(ok);
            Assert.AreEqual(NetworkChannel.Unreliable, resolved);
        }

        [Test]
        public void NoCompatibleChannel_ReturnsFalse()
        {
            bool ok = NetworkChannelNegotiation.TryResolve(
                NetworkChannel.Unreliable, NetworkChannelFlags.Reliable, out _);

            Assert.IsFalse(ok);
        }

        [Test]
        public void LocalLoop_AllChannelsMapToReliableId()
        {
            using var transport = new LocalLoopTransport();
            int reliableId = transport.GetChannelId(NetworkChannel.Reliable);

            Assert.AreEqual(reliableId, transport.GetChannelId(NetworkChannel.Unreliable));
            Assert.AreEqual(reliableId, transport.GetChannelId(NetworkChannel.ReliableUnordered));
            Assert.AreEqual(reliableId, transport.GetChannelId(NetworkChannel.UnreliableSequenced));
        }
    }
}
