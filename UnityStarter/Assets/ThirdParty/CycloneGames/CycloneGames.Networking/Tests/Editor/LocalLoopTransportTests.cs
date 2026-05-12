using System;
using CycloneGames.Networking.Transports;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class LocalLoopTransportTests
    {
        [Test]
        public void PollEvents_Dispatches_Client_To_Server_Payload()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();

            byte received = 0;
            int receiveCount = 0;
            int receivedChannel = -1;

            server.OnDataReceived += (_, payload, channelId) =>
            {
                receiveCount++;
                received = payload.Array![payload.Offset];
                receivedChannel = channelId;
            };

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            byte[] data = { 42 };
            client.Send(null, new ArraySegment<byte>(data), client.GetChannelId(NetworkChannel.Reliable));
            server.PollEvents();

            Assert.AreEqual(1, receiveCount);
            Assert.AreEqual(42, received);
            Assert.AreEqual(server.GetChannelId(NetworkChannel.Reliable), receivedChannel);
        }

        [Test]
        public void PollEvents_Dispatches_Server_To_Client_Payload()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();

            byte received = 0;
            int receiveCount = 0;

            client.OnDataReceived += (_, payload, _) =>
            {
                receiveCount++;
                received = payload.Array![payload.Offset];
            };

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            byte[] data = { 7 };
            server.Send(null, new ArraySegment<byte>(data), server.GetChannelId(NetworkChannel.Unreliable));
            client.PollEvents();

            Assert.AreEqual(1, receiveCount);
            Assert.AreEqual(7, received);
        }
    }
}
