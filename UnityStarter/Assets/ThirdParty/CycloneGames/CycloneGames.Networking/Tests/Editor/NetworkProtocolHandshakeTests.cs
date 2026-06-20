using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkProtocolHandshakeTests
    {
        private readonly struct TestHandshake : INetworkProtocolHandshake
        {
            public TestHandshake(ulong fingerprint, byte current, byte min, ulong domainHash)
            {
                ProtocolFingerprint = fingerprint;
                CurrentProtocolVersion = current;
                MinimumSupportedProtocolVersion = min;
                DomainStateHash = domainHash;
            }

            public ulong ProtocolFingerprint { get; }
            public byte CurrentProtocolVersion { get; }
            public byte MinimumSupportedProtocolVersion { get; }
            public ulong DomainStateHash { get; }
        }

        private struct ProtocolTestMessage
        {
            public int Value;
        }

        private const ushort RangeMin = 21000;
        private const ushort RangeMax = 21099;

        private static NetworkModuleProtocol CreateLocal(byte current = 3, byte min = 2)
        {
            var builder = new NetworkProtocolManifestBuilder(
                "CycloneGames.Tests.HandshakeModule",
                RangeMin,
                RangeMax,
                NetworkMessageKind.Module)
            {
                ProtocolId = "CycloneGames.Tests.Handshake",
                CurrentVersion = current,
                MinimumSupportedVersion = min
            };

            builder.AddMessage<ProtocolTestMessage>(RangeMin);

            return new NetworkModuleProtocol(builder.Build(), NetworkProtocolVersion.Create(current, min));
        }

        [Test]
        public void Negotiate_MatchingFingerprintAndVersion_Compatible()
        {
            NetworkModuleProtocol local = CreateLocal();
            var remote = new TestHandshake(local.Fingerprint, 3, 2, 0UL);

            Assert.AreEqual(NetworkHandshakeResult.Compatible, NetworkProtocolHandshake.Negotiate(remote, local));
        }

        [Test]
        public void Negotiate_FingerprintMismatch_Reported()
        {
            NetworkModuleProtocol local = CreateLocal();
            var remote = new TestHandshake(local.Fingerprint ^ 0xFFUL, 3, 2, 0UL);

            Assert.AreEqual(NetworkHandshakeResult.FingerprintMismatch, NetworkProtocolHandshake.Negotiate(remote, local));
        }

        [Test]
        public void Negotiate_VersionDisjoint_Incompatible()
        {
            NetworkModuleProtocol local = CreateLocal(current: 2, min: 2);
            var remote = new TestHandshake(local.Fingerprint, 5, 4, 0UL);

            Assert.AreEqual(NetworkHandshakeResult.VersionIncompatible, NetworkProtocolHandshake.Negotiate(remote, local));
        }

        [Test]
        public void Negotiate_ZeroFingerprint_Malformed()
        {
            NetworkModuleProtocol local = CreateLocal();
            var remote = new TestHandshake(0UL, 3, 2, 0UL);

            Assert.AreEqual(NetworkHandshakeResult.Malformed, NetworkProtocolHandshake.Negotiate(remote, local));
        }

        [Test]
        public void Negotiate_DomainStateHash_RespectsRequirement()
        {
            NetworkModuleProtocol local = CreateLocal();
            var remote = new TestHandshake(local.Fingerprint, 3, 2, 111UL);

            Assert.AreEqual(
                NetworkHandshakeResult.DomainStateMismatch,
                NetworkProtocolHandshake.Negotiate(remote, local, localDomainStateHash: 222UL, requireDomainStateMatch: true));

            Assert.AreEqual(
                NetworkHandshakeResult.Compatible,
                NetworkProtocolHandshake.Negotiate(remote, local, localDomainStateHash: 111UL, requireDomainStateMatch: true));

            // Ignored when not required.
            Assert.AreEqual(
                NetworkHandshakeResult.Compatible,
                NetworkProtocolHandshake.Negotiate(remote, local, localDomainStateHash: 222UL, requireDomainStateMatch: false));
        }
    }
}
