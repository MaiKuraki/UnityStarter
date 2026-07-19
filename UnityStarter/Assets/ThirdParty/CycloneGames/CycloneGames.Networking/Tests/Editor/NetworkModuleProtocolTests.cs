using System;
using CycloneGames.Networking.Security;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkProtocolVersionTests
    {
        [Test]
        public void Supports_WithinWindow_ReturnsTrue()
        {
            var version = new NetworkProtocolVersion(current: 3, minimumSupported: 2);

            Assert.IsTrue(version.Supports(2));
            Assert.IsTrue(version.Supports(3));
        }

        [Test]
        public void Supports_OutsideWindow_ReturnsFalse()
        {
            var version = new NetworkProtocolVersion(current: 3, minimumSupported: 2);

            Assert.IsFalse(version.Supports(1));
            Assert.IsFalse(version.Supports(4));
        }

        [Test]
        public void IsCompatibleWith_OverlappingWindows_ReturnsTrue()
        {
            var local = new NetworkProtocolVersion(3, 2);
            var remote = new NetworkProtocolVersion(2, 1);

            Assert.IsTrue(local.IsCompatibleWith(remote));
            Assert.IsTrue(remote.IsCompatibleWith(local));
        }

        [Test]
        public void IsCompatibleWith_DisjointWindows_ReturnsFalse()
        {
            var local = new NetworkProtocolVersion(2, 2);
            var remote = new NetworkProtocolVersion(5, 4);

            Assert.IsFalse(local.IsCompatibleWith(remote));
        }

        [Test]
        public void Ctor_InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkProtocolVersion(2, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkProtocolVersion(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkProtocolVersion(3, 0));
        }
    }

    public sealed class NetworkModuleProtocolTests
    {
        private const ushort RangeMin = 20000;
        private const ushort RangeMax = 20099;
        private const ushort PrimaryMessageId = RangeMin;

        private static NetworkModuleProtocol CreateProtocol()
        {
            var builder = new NetworkProtocolManifestBuilder(
                "CycloneGames.Tests.ProtocolModule",
                RangeMin,
                RangeMax)
            {
                ProtocolId = "CycloneGames.Tests.Protocol",
                CurrentVersion = 3,
                MinimumSupportedVersion = 2
            };

            builder.AddMessage("ProtocolTestMessage:v1", PrimaryMessageId, 0x6EB11280CBB2205FUL);

            return new NetworkModuleProtocol(builder.Build());
        }

        [Test]
        public void ContainsMessageId_RespectsRange()
        {
            NetworkModuleProtocol protocol = CreateProtocol();

            Assert.IsTrue(protocol.ContainsMessageId(RangeMin));
            Assert.IsTrue(protocol.ContainsMessageId(RangeMax));
            Assert.IsFalse(protocol.ContainsMessageId((ushort)(RangeMax + 1)));
        }

        [Test]
        public void IsSupportedProtocolVersion_DelegatesToVersionWindow()
        {
            NetworkModuleProtocol protocol = CreateProtocol();

            Assert.IsTrue(protocol.IsSupportedProtocolVersion(2));
            Assert.IsTrue(protocol.IsSupportedProtocolVersion(3));
            Assert.IsFalse(protocol.IsSupportedProtocolVersion(1));
        }

        [Test]
        public void Register_RegistersManifestMessagesIntoCatalog()
        {
            NetworkModuleProtocol protocol = CreateProtocol();
            var catalog = new NetworkMessageCatalog();

            protocol.Register(catalog);

            Assert.AreEqual(1, catalog.MessageCount);
            Assert.AreEqual(1, catalog.ManifestCount);
            Assert.IsTrue(catalog.TryGet(PrimaryMessageId, out _));
            Assert.AreEqual(protocol.Manifest.Fingerprint, protocol.Fingerprint);
        }

        [Test]
        public void Register_DoesNotPartiallyMutateCatalog_WhenRangeConflicts()
        {
            NetworkModuleProtocol protocol = CreateProtocol();
            var catalog = new NetworkMessageCatalog();
            NetworkProtocolManifest conflict = new NetworkProtocolManifestBuilder(
                    "CycloneGames.Tests.ConflictingModule",
                    (ushort)(RangeMin + 50),
                    (ushort)(RangeMax + 50))
                .AddMessage(
                    "ProtocolTestMessage:v1",
                    (ushort)(RangeMin + 50),
                    0x6EB11280CBB2205FUL)
                .Build();
            Assert.IsTrue(catalog.TryRegisterProtocolManifest(conflict));

            Assert.Throws<InvalidOperationException>(() => protocol.Register(catalog));

            Assert.AreEqual(1, catalog.MessageCount);
            Assert.AreEqual(1, catalog.ManifestCount);
        }

        [Test]
        public void Build_Rejects_NonAscii_Stable_Identifiers()
        {
            var builder = new NetworkProtocolManifestBuilder("CycloneGames.Tests.ProtocolModule", RangeMin, RangeMax)
            {
                ProtocolId = "CycloneGames.Tests.协议"
            };
            builder.AddMessage("ProtocolTestMessage:v1", PrimaryMessageId, 0x6EB11280CBB2205FUL);

            Assert.Throws<ArgumentException>(() => builder.Build());
        }

        [Test]
        public void Build_Rejects_Payload_Budget_Larger_Than_Maximum_Frame()
        {
            var builder = new NetworkProtocolManifestBuilder("CycloneGames.Tests.ProtocolModule", RangeMin, RangeMax);
            builder.AddMessage(
                "ProtocolTestMessage:v1",
                PrimaryMessageId,
                0x6EB11280CBB2205FUL,
                maxPayloadSize: NetworkConstants.MaxMTU - NetworkWireProtocol.HeaderLength + 1);

            Assert.Throws<ArgumentException>(() => builder.Build());
        }
    }

    public sealed class NetworkMessageHandlerRegistryTests
    {
        [Test]
        public void Dispatch_ExposesValidatedHeaderDirectionAndCanonicalBytes()
        {
            const ushort messageId = 1234;
            var registry = new NetworkMessageHandlerRegistry();
            byte[] bytes = { 3, 1, 4, 1, 5 };
            var header = new NetworkEnvelopeHeader(
                messageId,
                NetworkChannel.Unreliable,
                bytes.Length,
                sequence: 27u,
                checksum: 91u,
                flags: NetworkMessageFlags.Ordered);
            int callCount = 0;

            using NetworkMessageHandlerLease lease = registry.Register(
                messageId,
                (in NetworkMessagePayload payload) =>
                {
                    callCount++;
                    Assert.AreEqual(messageId, payload.Header.MessageId);
                    Assert.AreEqual(NetworkChannel.Unreliable, payload.Header.Channel);
                    Assert.AreEqual(27u, payload.Header.Sequence);
                    Assert.AreEqual(NetworkMessageDirection.ServerToClient, payload.Direction);
                    Assert.IsNull(payload.Connection);
                    Assert.IsTrue(payload.Bytes.SequenceEqual(bytes));
                });

            var message = new NetworkMessagePayload(
                null,
                NetworkMessageDirection.ServerToClient,
                in header,
                bytes);

            Assert.IsTrue(registry.TryDispatch(in message));
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void Register_DuplicateMessageId_FailsFast()
        {
            var registry = new NetworkMessageHandlerRegistry();
            using NetworkMessageHandlerLease lease = registry.Register(7, IgnorePayload);

            Assert.Throws<InvalidOperationException>(() => registry.Register(7, IgnorePayload));
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void Register_CapacityIsBoundedAndReleasedLeaseFreesSlot()
        {
            var registry = new NetworkMessageHandlerRegistry(capacity: 1, maxHandlers: 1);
            NetworkMessageHandlerLease first = registry.Register(7, IgnorePayload);

            Assert.Throws<InvalidOperationException>(() => registry.Register(8, IgnorePayload));
            first.Dispose();

            using NetworkMessageHandlerLease second = registry.Register(8, IgnorePayload);
            Assert.AreEqual(1, registry.Count);
            Assert.AreEqual(1, registry.MaxHandlers);
        }

        [Test]
        public void Lease_StaleCopyAndRepeatedDispose_CannotRemoveNewGeneration()
        {
            var registry = new NetworkMessageHandlerRegistry();
            NetworkMessageHandlerLease first = registry.Register(9, IgnorePayload);
            NetworkMessageHandlerLease staleCopy = first;
            first.Dispose();

            int secondCallCount = 0;
            NetworkMessageHandlerLease second = registry.Register(
                9,
                (in NetworkMessagePayload _) => secondCallCount++);
            staleCopy.Dispose();

            byte[] bytes = { 1 };
            var header = new NetworkEnvelopeHeader(9, NetworkChannel.Reliable, 1, 1u, 0u);
            var message = new NetworkMessagePayload(
                null,
                NetworkMessageDirection.ClientToServer,
                in header,
                bytes);
            Assert.IsTrue(registry.TryDispatch(in message));
            Assert.AreEqual(1, secondCallCount);

            second.Dispose();
            second.Dispose();
            Assert.IsFalse(registry.TryDispatch(in message));
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void Dispatch_WarmedEditorMonoPath_DoesNotAllocate()
        {
            var registry = new NetworkMessageHandlerRegistry();
            int callCount = 0;
            using NetworkMessageHandlerLease lease = registry.Register(
                11,
                (in NetworkMessagePayload _) => callCount++);
            byte[] bytes = { 8, 9 };
            var header = new NetworkEnvelopeHeader(11, NetworkChannel.Reliable, bytes.Length, 1u, 0u);
            var message = new NetworkMessagePayload(
                null,
                NetworkMessageDirection.ClientToServer,
                in header,
                bytes);

            registry.TryDispatch(in message);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2048; i++)
                registry.TryDispatch(in message);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated);
            Assert.AreEqual(2049, callCount);
        }

        private static void IgnorePayload(in NetworkMessagePayload payload)
        {
        }
    }
}
