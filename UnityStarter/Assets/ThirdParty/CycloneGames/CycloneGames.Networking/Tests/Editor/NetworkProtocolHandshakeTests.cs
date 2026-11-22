using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkProtocolHandshakeTests
    {
        private readonly struct TestHandshake : INetworkProtocolHandshakeMessage
        {
            public TestHandshake(ulong fingerprint, ulong domainHash)
            {
                ProtocolFingerprint = fingerprint;
                DomainStateHash = domainHash;
            }

            public ulong ProtocolFingerprint { get; }
            public ulong DomainStateHash { get; }
        }

        private const ushort RangeMin = 21000;
        private const ushort RangeMax = 21099;

        private static NetworkModuleProtocol CreateLocal(string contractId, ulong schemaHash)
        {
            var builder = new NetworkProtocolManifestBuilder(
                "CycloneGames.Tests.HandshakeModule", RangeMin, RangeMax)
            {
                ProtocolId = "CycloneGames.Tests.Handshake"
            };
            builder.AddMessage(contractId, RangeMin, schemaHash);
            return new NetworkModuleProtocol(builder.Build());
        }

        [Test]
        public void Negotiate_MatchingFingerprint_Compatible()
        {
            NetworkModuleProtocol local = CreateLocal("ProtocolTestMessage", 0xDFBC8C7F244165D4UL);
            var remote = new TestHandshake(local.Fingerprint, 0UL);
            Assert.AreEqual(NetworkHandshakeResult.Compatible,
                NetworkProtocolHandshake.Negotiate(remote, local));
            Assert.IsTrue(local.MatchesFingerprint(remote.ProtocolFingerprint));
        }

        [Test]
        public void ContractIdentity_Changes_Fingerprint()
        {
            NetworkModuleProtocol first = CreateLocal("ProtocolTestMessage", 0xDFBC8C7F244165D4UL);
            NetworkModuleProtocol second = CreateLocal("ProtocolTestMessageTwo",
                0xB87BCA7EB23BA36EUL);
            Assert.AreNotEqual(first.Fingerprint, second.Fingerprint);
            Assert.IsFalse(first.MatchesFingerprint(second.Fingerprint));
        }

        [Test]
        public void Negotiate_FingerprintMismatch_Reported()
        {
            NetworkModuleProtocol local = CreateLocal("ProtocolTestMessage", 0xDFBC8C7F244165D4UL);
            var remote = new TestHandshake(local.Fingerprint ^ 0xFFUL, 0UL);
            Assert.AreEqual(NetworkHandshakeResult.FingerprintMismatch,
                NetworkProtocolHandshake.Negotiate(remote, local));
        }

        [Test]
        public void Negotiate_ZeroFingerprint_Malformed()
        {
            NetworkModuleProtocol local = CreateLocal("ProtocolTestMessage", 0xDFBC8C7F244165D4UL);
            var remote = new TestHandshake(0UL, 0UL);
            Assert.AreEqual(NetworkHandshakeResult.Malformed,
                NetworkProtocolHandshake.Negotiate(remote, local));
            Assert.IsFalse(NetworkProtocolHandshake.IsWellFormed(remote));
        }

        [Test]
        public void Negotiate_DomainStateHash_RespectsRequirement()
        {
            NetworkModuleProtocol local = CreateLocal("ProtocolTestMessage", 0xDFBC8C7F244165D4UL);
            var remote = new TestHandshake(local.Fingerprint, 111UL);

            Assert.AreEqual(NetworkHandshakeResult.DomainStateMismatch,
                NetworkProtocolHandshake.Negotiate(remote, local,
                    localDomainStateHash: 222UL, requireDomainStateMatch: true));
            Assert.AreEqual(NetworkHandshakeResult.Compatible,
                NetworkProtocolHandshake.Negotiate(remote, local,
                    localDomainStateHash: 111UL, requireDomainStateMatch: true));
            Assert.AreEqual(NetworkHandshakeResult.Compatible,
                NetworkProtocolHandshake.Negotiate(remote, local,
                    localDomainStateHash: 222UL, requireDomainStateMatch: false));
        }
    }
}
