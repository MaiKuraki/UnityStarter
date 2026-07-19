using System;
using CycloneGames.Networking.Transports;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class LocalLoopTransportTests
    {
        [Test]
        public void Available_Is_True_In_Editor()
        {
            using var transport = new LocalLoopTransport();

            Assert.IsTrue(transport.Available);
        }

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
            server.Send(null, new ArraySegment<byte>(data), server.GetChannelId(NetworkChannel.Reliable));
            client.PollEvents();

            Assert.AreEqual(1, receiveCount);
            Assert.AreEqual(7, received);
        }

        [Test]
        public void PollEvents_Uses_Bounded_Work_Budget()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();
            int received = 0;
            server.OnDataReceived += (_, _, _) => received++;

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            byte[] data = { 1 };
            int channel = client.GetChannelId(NetworkChannel.Reliable);
            for (int i = 0; i < 300; i++)
                Assert.AreEqual(NetworkSendStatus.Queued, client.Send(null, new ArraySegment<byte>(data), channel).Status);

            server.PollEvents();
            Assert.AreEqual(256, received);

            server.PollEvents();
            Assert.AreEqual(300, received);
        }

        [Test]
        public void Send_Returns_Backpressure_When_Queue_Is_Full()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();
            server.StartServer();
            client.StartClient(string.Empty);

            byte[] data = { 1 };
            int channel = client.GetChannelId(NetworkChannel.Reliable);
            for (int i = 0; i < 1024; i++)
                Assert.AreEqual(NetworkSendStatus.Queued, client.Send(null, new ArraySegment<byte>(data), channel).Status);

            NetworkSendResult rejected = client.Send(null, new ArraySegment<byte>(data), channel);
            Assert.AreEqual(NetworkSendStatus.Backpressure, rejected.Status);
        }

        [Test]
        public void Capabilities_Match_ReliableOnly_ChannelContract()
        {
            using var transport = new LocalLoopTransport();

            Assert.AreEqual(NetworkChannelFlags.Reliable, transport.Capabilities.SupportedChannels);
            Assert.AreEqual(0, transport.Capabilities.MaxUnreliablePacketSize);
            Assert.AreEqual(0, transport.GetMaxPacketSize(transport.GetChannelId(NetworkChannel.Unreliable)));
        }

        [Test]
        public void Disconnect_Event_Exposes_Disconnected_RemotePeer()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();
            INetConnection disconnected = null;

            server.OnClientDisconnected += connection => disconnected = connection;
            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();
            client.PollEvents();

            client.Stop();
            server.PollEvents();

            Assert.IsNotNull(disconnected);
            Assert.IsFalse(disconnected.IsConnected);
            Assert.Greater(disconnected.ConnectionId, 0);
        }

        [Test]
        public void Duplicate_Peer_Registration_FailsFast()
        {
            using var firstServer = new LocalLoopTransport();
            using var secondServer = new LocalLoopTransport();

            firstServer.StartServer();

            Assert.Throws<InvalidOperationException>(() => secondServer.StartServer());
        }

        [Test]
        public void PollEvents_Allows_Connection_Callback_To_Stop_Transport()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();
            server.OnClientConnected += _ => server.Stop();

            server.StartServer();
            client.StartClient(string.Empty);

            Assert.DoesNotThrow(server.PollEvents);
            Assert.IsFalse(server.IsRunning);
        }

        [Test]
        public void PollEvents_Stops_Dispatch_After_Data_Callback_Stops_Transport()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();
            int received = 0;
            server.OnDataReceived += (_, _, _) =>
            {
                received++;
                server.Stop();
            };

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            byte[] data = { 1 };
            int channel = client.GetChannelId(NetworkChannel.Reliable);
            Assert.AreEqual(NetworkSendStatus.Queued, client.Send(null, new ArraySegment<byte>(data), channel).Status);
            Assert.AreEqual(NetworkSendStatus.Queued, client.Send(null, new ArraySegment<byte>(data), channel).Status);

            Assert.DoesNotThrow(server.PollEvents);
            Assert.AreEqual(1, received);
            Assert.IsFalse(server.IsRunning);
        }

        [Test]
        public void PeerStop_Drops_Queued_Data_Before_Disconnect_Callback()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();
            int received = 0;
            int disconnected = 0;
            server.OnDataReceived += (_, _, _) => received++;
            server.OnClientDisconnected += _ => disconnected++;

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            byte[] data = { 1 };
            int channel = client.GetChannelId(NetworkChannel.Reliable);
            Assert.AreEqual(NetworkSendStatus.Queued, client.Send(null, new ArraySegment<byte>(data), channel).Status);
            client.Stop();

            server.PollEvents();

            Assert.AreEqual(1, disconnected);
            Assert.AreEqual(0, received);
        }

        [Test]
        public void RestartedPeer_DoesNotReceive_PreviousSession_Data()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();
            int received = 0;
            client.OnDataReceived += (_, _, _) => received++;

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();
            client.PollEvents();

            byte[] data = { 1 };
            int channel = server.GetChannelId(NetworkChannel.Reliable);
            Assert.AreEqual(NetworkSendStatus.Queued, server.Send(null, new ArraySegment<byte>(data), channel).Status);

            client.Stop();
            client.StartClient(string.Empty);
            client.PollEvents();

            Assert.AreEqual(0, received);
        }

        [Test]
        public void PeerRestart_BetweenPolls_Publishes_DisconnectThenFreshConnection()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();
            INetConnection first = null;
            INetConnection second = null;
            INetConnection disconnected = null;
            int connectedCount = 0;
            server.OnClientConnected += connection =>
            {
                connectedCount++;
                if (first == null) first = connection;
                else second = connection;
            };
            server.OnClientDisconnected += connection => disconnected = connection;

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();
            first.PlayerId = 99UL;

            client.Stop();
            client.StartClient(string.Empty);
            server.PollEvents();

            Assert.AreEqual(2, connectedCount);
            Assert.AreSame(first, disconnected);
            Assert.IsFalse(first.IsConnected);
            Assert.IsNotNull(second);
            Assert.IsTrue(second.IsConnected);
            Assert.AreNotEqual(first.ConnectionId, second.ConnectionId);
            Assert.AreEqual(0UL, second.PlayerId);
        }

        [Test]
        public void Stale_Connection_Cannot_Send_To_Or_Disconnect_Fresh_Peer_Session()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();
            INetConnection first = null;
            INetConnection second = null;
            server.OnClientConnected += connection =>
            {
                if (first == null) first = connection;
                else second = connection;
            };

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();
            client.PollEvents();

            client.Stop();
            client.StartClient(string.Empty);
            server.PollEvents();
            client.PollEvents();

            byte[] data = { 1 };
            int channel = server.GetChannelId(NetworkChannel.Reliable);
            NetworkSendResult staleSend = server.Send(first, new ArraySegment<byte>(data), channel);

            Assert.AreEqual(NetworkSendStatus.NotConnected, staleSend.Status);
            server.Disconnect(first);
            Assert.IsTrue(server.IsRunning);
            Assert.AreEqual(NetworkSendStatus.Queued, server.Send(second, new ArraySegment<byte>(data), channel).Status);
        }

        [Test]
        public void Peer_Restart_During_Disconnect_Callback_Does_Not_Publish_Intermediate_Connection()
        {
            using var server = new LocalLoopTransport();
            using var client = new LocalLoopTransport();
            int connected = 0;
            int disconnected = 0;
            bool restartOnce = true;
            server.OnClientConnected += _ => connected++;
            server.OnClientDisconnected += _ =>
            {
                disconnected++;
                if (!restartOnce) return;

                restartOnce = false;
                client.Stop();
                client.StartClient(string.Empty);
            };

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            client.Stop();
            client.StartClient(string.Empty);
            server.PollEvents();

            Assert.AreEqual(1, connected);
            Assert.AreEqual(1, disconnected);

            server.PollEvents();

            Assert.AreEqual(2, connected);
            Assert.AreEqual(1, disconnected);
        }

        [Test]
        public void StartClient_Rejects_NonDefault_Channel_Name()
        {
            using var client = new LocalLoopTransport();

            Assert.Throws<ArgumentException>(() => client.StartClient("unsupported-channel"));
            Assert.IsFalse(client.IsRunning);
        }
    }
}
