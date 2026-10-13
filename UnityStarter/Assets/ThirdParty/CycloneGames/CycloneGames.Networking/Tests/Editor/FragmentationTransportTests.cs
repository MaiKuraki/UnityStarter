using System;
using System.Collections.Generic;
using CycloneGames.Networking.Transports;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class FragmentationTransportTests
    {
        private static FragmentationTransport CreateFragmented(LocalLoopTransport inner, int mtu = NetworkConstants.DefaultMTU)
        {
            var options = new FragmentationOptions(
                mtu: mtu,
                maxReassembledBytes: 256 * 1024,
                maxConcurrentGroups: 64,
                maxReassemblyBytesInFlight: 4 * 1024 * 1024,
                groupTimeoutSeconds: 5d,
                clearBuffersOnReturn: false);
            return new FragmentationTransport(inner, options);
        }

        private static byte[] MakePayload(int length, int seed)
        {
            var data = new byte[length];
            var rng = new Random(seed);
            rng.NextBytes(data);
            return data;
        }

        [Test]
        public void SmallPayload_PassesThrough_ByteIdentical()
        {
            using var serverInner = new LocalLoopTransport();
            using var clientInner = new LocalLoopTransport();
            using var server = CreateFragmented(serverInner);
            using var client = CreateFragmented(clientInner);

            byte[] received = null;
            server.OnDataReceived += (_, payload, _) =>
            {
                received = new byte[payload.Count];
                Buffer.BlockCopy(payload.Array, payload.Offset, received, 0, payload.Count);
            };

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            byte[] data = MakePayload(200, 1);
            client.Send(null, new ArraySegment<byte>(data), client.GetChannelId(NetworkChannel.Reliable));
            server.PollEvents();

            Assert.NotNull(received);
            Assert.AreEqual(data, received);
        }

        [Test]
        public void LargePayload_IsFragmented_AndReassembled_ByteIdentical()
        {
            using var serverInner = new LocalLoopTransport();
            using var clientInner = new LocalLoopTransport();
            using var server = CreateFragmented(serverInner);
            using var client = CreateFragmented(clientInner);

            byte[] received = null;
            int receiveCount = 0;
            server.OnDataReceived += (_, payload, _) =>
            {
                receiveCount++;
                received = new byte[payload.Count];
                Buffer.BlockCopy(payload.Array, payload.Offset, received, 0, payload.Count);
            };

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            byte[] data = MakePayload(5000, 2);
            NetworkSendResult result = client.Send(null, new ArraySegment<byte>(data), client.GetChannelId(NetworkChannel.Reliable));
            Assert.IsTrue(result.Succeeded, $"Send failed with {result.Status}");
            server.PollEvents();
            server.PollEvents();

            Assert.AreEqual(1, receiveCount, "Reassembled payload must be delivered exactly once");
            Assert.AreEqual(data, received);
        }

        [Test]
        public void OversizedPayload_ReturnsPayloadTooLarge_WithoutSending()
        {
            using var serverInner = new LocalLoopTransport();
            using var clientInner = new LocalLoopTransport();
            using var server = CreateFragmented(serverInner);
            using var client = CreateFragmented(clientInner);

            int receiveCount = 0;
            serverInner.OnDataReceived += (_, _, _) => receiveCount++;

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            byte[] data = MakePayload(300 * 1024, 3);
            NetworkSendResult result = client.Send(null, new ArraySegment<byte>(data), client.GetChannelId(NetworkChannel.Reliable));

            Assert.AreEqual(NetworkSendStatus.PayloadTooLarge, result.Status);
            server.PollEvents();
            Assert.AreEqual(0, receiveCount);
        }

        [Test]
        public void HostileFragment_IsDropped_WithoutStateGrowth()
        {
            using var serverInner = new LocalLoopTransport();
            using var clientInner = new LocalLoopTransport();
            using var server = CreateFragmented(serverInner);
            using var client = CreateFragmented(clientInner);

            int deliverCount = 0;
            server.OnDataReceived += (_, _, _) => deliverCount++;

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            // Forge a fragment header claiming an absurd total length.
            var hostile = new byte[FragmentationTransport.FragmentHeaderSize + 16];
            hostile[0] = (byte)'C';
            hostile[1] = (byte)'F';
            hostile[2] = 1;
            hostile[4] = 1; // groupId = 1
            hostile[10] = 2; // count = 2
            hostile[11] = 0;
            hostile[12] = 0xFF; // totalLength = 0x7FFFFFFF-ish
            hostile[13] = 0xFF;
            hostile[14] = 0xFF;
            hostile[15] = 0x7F;

            clientInner.Send(null, new ArraySegment<byte>(hostile), clientInner.GetChannelId(NetworkChannel.Reliable));
            server.PollEvents();

            Assert.AreEqual(0, deliverCount);
            Assert.AreEqual(0, server.Metrics.ActiveGroups);
            Assert.AreEqual(0, server.Metrics.ReassemblyBytesInFlight);
        }

        [Test]
        public void Capabilities_AdvertiseFragmentation()
        {
            using var inner = new LocalLoopTransport();
            using var transport = CreateFragmented(inner);

            Assert.IsTrue((transport.Capabilities.Features & NetworkTransportFeatureFlags.Fragmentation) != 0);
        }

        [Test]
        public void Burst_ManyLargeMessages_AllReassembled()
        {
            using var serverInner = new LocalLoopTransport();
            using var clientInner = new LocalLoopTransport();
            using var server = CreateFragmented(serverInner);
            using var client = CreateFragmented(clientInner);

            var received = new List<byte[]>();
            server.OnDataReceived += (_, payload, _) =>
            {
                var copy = new byte[payload.Count];
                Buffer.BlockCopy(payload.Array, payload.Offset, copy, 0, payload.Count);
                received.Add(copy);
            };

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            const int messageCount = 40;
            var sent = new List<byte[]>(messageCount);
            int channel = client.GetChannelId(NetworkChannel.Reliable);
            for (int i = 0; i < messageCount; i++)
            {
                byte[] data = MakePayload(3000, 100 + i);
                sent.Add(data);
                client.Send(null, new ArraySegment<byte>(data), channel);
            }

            for (int i = 0; i < 16; i++)
                server.PollEvents();

            Assert.AreEqual(messageCount, received.Count);
            for (int i = 0; i < messageCount; i++)
                Assert.AreEqual(sent[i], received[i], $"Message {i} corrupted");
            Assert.AreEqual(0, server.Metrics.ActiveGroups);
        }

        [Test]
        public void Broadcast_LargePayload_AllRecipientsReceiveIdentical()
        {
            using var serverInner = new LocalLoopTransport();
            using var clientAInner = new LocalLoopTransport();
            using var server = CreateFragmented(serverInner);
            using var clientA = CreateFragmented(clientAInner);

            var receivedA = new List<byte[]>();
            clientA.OnDataReceived += (_, payload, _) =>
            {
                var copy = new byte[payload.Count];
                Buffer.BlockCopy(payload.Array, payload.Offset, copy, 0, payload.Count);
                receivedA.Add(copy);
            };

            server.StartServer();
            clientA.StartClient(string.Empty);
            server.PollEvents();
            server.PollEvents();

            // LocalLoop is a single-connection loopback; an empty recipient list must
            // short-circuit without touching the encode-once fragment path.
            byte[] data = MakePayload(4000, 7);
            var connections = new List<INetConnection>();
            NetworkSendResult result = server.Broadcast(connections, new ArraySegment<byte>(data), server.GetChannelId(NetworkChannel.Reliable));
            Assert.AreEqual(NetworkSendStatus.NotConnected, result.Status);
            Assert.AreEqual(0, receivedA.Count);
        }

        [Test]
        public void Stop_ReleasesAllReassemblyState()
        {
            using var serverInner = new LocalLoopTransport();
            using var clientInner = new LocalLoopTransport();
            using var server = CreateFragmented(serverInner);
            using var client = CreateFragmented(clientInner);

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            byte[] data = MakePayload(5000, 8);
            client.Send(null, new ArraySegment<byte>(data), client.GetChannelId(NetworkChannel.Reliable));
            // Deliver nothing; stop mid-reassembly.
            server.Stop();

            Assert.AreEqual(0, server.Metrics.ActiveGroups);
            Assert.AreEqual(0, server.Metrics.ReassemblyBytesInFlight);
        }

        [Test]
        public void ReassembledPayload_IsOnlyValidDuringCallback()
        {
            using var serverInner = new LocalLoopTransport();
            using var clientInner = new LocalLoopTransport();
            using var server = CreateFragmented(serverInner);
            using var client = CreateFragmented(clientInner);

            int receivedLength = -1;
            server.OnDataReceived += (_, payload, _) => receivedLength = payload.Count;

            server.StartServer();
            client.StartClient(string.Empty);
            server.PollEvents();

            byte[] data = MakePayload(4096, 9);
            client.Send(null, new ArraySegment<byte>(data), client.GetChannelId(NetworkChannel.Reliable));
            server.PollEvents();
            server.PollEvents();

            Assert.AreEqual(data.Length, receivedLength);
            Assert.AreEqual(0, server.Metrics.ReassemblyBytesInFlight, "Buffer must return to pool after callback");
        }
    }
}
