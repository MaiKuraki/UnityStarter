using System;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkProtocolVersionTests
    {
        [Test]
        public void Supports_WithinWindow_ReturnsTrue()
        {
            var version = NetworkProtocolVersion.Create(current: 3, minimumSupported: 2);

            Assert.IsTrue(version.Supports(2));
            Assert.IsTrue(version.Supports(3));
        }

        [Test]
        public void Supports_OutsideWindow_ReturnsFalse()
        {
            var version = NetworkProtocolVersion.Create(current: 3, minimumSupported: 2);

            Assert.IsFalse(version.Supports(1));
            Assert.IsFalse(version.Supports(4));
        }

        [Test]
        public void IsCompatibleWith_OverlappingWindows_ReturnsTrue()
        {
            var local = NetworkProtocolVersion.Create(3, 2);
            var remote = NetworkProtocolVersion.Create(2, 1);

            Assert.IsTrue(local.IsCompatibleWith(remote));
            Assert.IsTrue(remote.IsCompatibleWith(local));
        }

        [Test]
        public void IsCompatibleWith_DisjointWindows_ReturnsFalse()
        {
            var local = NetworkProtocolVersion.Create(2, 2);
            var remote = NetworkProtocolVersion.Create(5, 4);

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
        private struct ProtocolTestMessage
        {
            public int Value;
        }

        private const ushort RangeMin = 20000;
        private const ushort RangeMax = 20099;
        private const ushort PrimaryMessageId = RangeMin;

        private static NetworkModuleProtocol CreateProtocol()
        {
            var builder = new NetworkProtocolManifestBuilder(
                "CycloneGames.Tests.ProtocolModule",
                RangeMin,
                RangeMax,
                NetworkMessageKind.Module)
            {
                ProtocolId = "CycloneGames.Tests.Protocol",
                CurrentVersion = 3,
                MinimumSupportedVersion = 2
            };

            builder.AddMessage<ProtocolTestMessage>(PrimaryMessageId);

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

            Assert.AreEqual(1, catalog.Count);
            Assert.IsTrue(catalog.TryGet(PrimaryMessageId, out _));
            Assert.AreEqual(protocol.Manifest.Fingerprint, protocol.Fingerprint);
        }

        [Test]
        public void RegisterMessage_IsIdempotentForIdenticalDescriptor()
        {
            NetworkModuleProtocol protocol = CreateProtocol();
            var catalog = new NetworkMessageCatalog();
            ushort extensionId = (ushort)(RangeMin + 1);

            protocol.RegisterMessage<ProtocolTestMessage>(catalog, extensionId);
            Assert.DoesNotThrow(() => protocol.RegisterMessage<ProtocolTestMessage>(catalog, extensionId));

            Assert.AreEqual(1, catalog.Count);
        }

        [Test]
        public void RegisterMessage_OutsideRange_Throws()
        {
            NetworkModuleProtocol protocol = CreateProtocol();
            var catalog = new NetworkMessageCatalog();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => protocol.RegisterMessage<ProtocolTestMessage>(catalog, (ushort)(RangeMin - 1)));
        }
    }
}
