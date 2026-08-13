using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Networking;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Networking.Tests.Editor
{
    public sealed class GameplayNetworkReplicationTests
    {
        [Test]
        public void AuthorityResolver_AssignsServerOwnerAndSimulatedRoles()
        {
            var resolver = new ServerAuthoritativeGameplayAuthorityResolver();
            var actor = CreateActor(ownerConnectionId: 2);

            Assert.AreEqual(
                GameplayNetworkAuthorityRole.ServerAuthority,
                resolver.GetRole(new GameplayNetworkAuthorityContext(true, false, 0), actor));

            Assert.AreEqual(
                GameplayNetworkAuthorityRole.AutonomousProxy,
                resolver.GetRole(new GameplayNetworkAuthorityContext(false, true, 2), actor));

            Assert.AreEqual(
                GameplayNetworkAuthorityRole.SimulatedProxy,
                resolver.GetRole(new GameplayNetworkAuthorityContext(false, true, 3), actor));

            Assert.IsTrue(resolver.CanWriteAuthoritativeState(new GameplayNetworkAuthorityContext(true, false, 0), actor));
            Assert.IsTrue(resolver.CanSendOwnerInput(new GameplayNetworkAuthorityContext(false, true, 2), actor));
            Assert.IsFalse(resolver.CanSendOwnerInput(new GameplayNetworkAuthorityContext(false, true, 3), actor));
        }

        [Test]
        public void AuthorityResolver_DoesNotTreatZeroIdentifiersAsOwnership()
        {
            var resolver = new ServerAuthoritativeGameplayAuthorityResolver();
            var unownedActor = CreateActor(ownerConnectionId: 0);
            var context = new GameplayNetworkAuthorityContext(false, true, 0);

            Assert.AreEqual(GameplayNetworkAuthorityRole.SimulatedProxy, resolver.GetRole(context, unownedActor));
            Assert.IsFalse(resolver.CanSendOwnerInput(context, unownedActor));

            var observerResolver = new GameplayNetworkObserverResolver();
            var candidates = new INetConnection[] { new TestConnection(0) };
            var results = new List<INetConnection>(1);
            int count = observerResolver.ResolveObservers(
                new GameplayReplicationContext(unownedActor, GameplayReplicationPolicy.OwnerReliable),
                candidates,
                observerSource: null,
                results: results);

            Assert.AreEqual(0, count);
        }

        [Test]
        public void AuthorityResolver_RejectsNegativeOwnerIdentity()
        {
            var resolver = new ServerAuthoritativeGameplayAuthorityResolver();
            var actor = CreateActor(ownerConnectionId: -1);

            Assert.AreEqual(
                GameplayNetworkAuthorityRole.None,
                resolver.GetRole(new GameplayNetworkAuthorityContext(false, true, 1), actor));
            Assert.IsFalse(
                resolver.CanSendOwnerInput(new GameplayNetworkAuthorityContext(false, true, 1), actor));
        }

        [Test]
        public void AuthorityResolver_ReturnsNoneOutsideServerOrClientContext()
        {
            var resolver = new ServerAuthoritativeGameplayAuthorityResolver();
            var actor = CreateActor(ownerConnectionId: 1);
            var context = new GameplayNetworkAuthorityContext(false, false, 1);

            Assert.AreEqual(GameplayNetworkAuthorityRole.None, resolver.GetRole(context, actor));
            Assert.IsFalse(resolver.CanWriteAuthoritativeState(context, actor));
            Assert.IsFalse(resolver.CanSendOwnerInput(context, actor));
        }

        [Test]
        public void ObserverResolver_OwnerOnly_ReturnsOwner()
        {
            var resolver = new GameplayNetworkObserverResolver();
            var registry = new GameplayNetworkObserverRegistry();
            var owner = new TestConnection(2);
            var other = new TestConnection(3);
            var candidates = new INetConnection[] { owner, other };
            var results = new List<INetConnection>(4);
            var context = new GameplayReplicationContext(CreateActor(ownerConnectionId: 2), GameplayReplicationPolicy.OwnerReliable);

            int count = resolver.ResolveObservers(context, candidates, registry, results);

            Assert.AreEqual(1, count);
            Assert.AreSame(owner, results[0]);
        }

        [Test]
        public void ObserverResolver_AreaPolicy_FiltersByDistanceAndLayer()
        {
            var resolver = new GameplayNetworkObserverResolver();
            var registry = new GameplayNetworkObserverRegistry();
            var near = new TestConnection(2);
            var far = new TestConnection(3);
            var wrongLayer = new TestConnection(4);
            var candidates = new INetConnection[] { near, far, wrongLayer };
            var results = new List<INetConnection>(4);

            registry.SetObserver(2, new NetworkVector3(3f, 0f, 4f), 100f, 0b0001u);
            registry.SetObserver(3, new NetworkVector3(20f, 0f, 0f), 100f, 0b0001u);
            registry.SetObserver(4, new NetworkVector3(1f, 0f, 0f), 100f, 0b0100u);

            var actor = new NetworkedGameplayActor(
                10,
                ownerConnectionId: 99,
                ownerPlayerId: 0UL,
                teamId: 0,
                interestLayerMask: 0b0001u,
                alwaysRelevant: false,
                interestPosition: NetworkVector3.Zero);
            var context = new GameplayReplicationContext(actor, GameplayReplicationPolicy.AreaUnreliable(10f, layerMask: 0b0001u));

            int count = resolver.ResolveObservers(context, candidates, registry, results);

            Assert.AreEqual(1, count);
            Assert.AreSame(near, results[0]);
        }

        [Test]
        public void ObserverResolver_UsesTheSmallerPolicyAndObserverRadius()
        {
            var resolver = new GameplayNetworkObserverResolver();
            var registry = new GameplayNetworkObserverRegistry();
            var connection = new TestConnection(2);
            var candidates = new INetConnection[] { connection };
            var results = new List<INetConnection>(1);
            registry.SetObserver(2, new NetworkVector3(8f, 0f, 0f), radius: 5f);
            var context = new GameplayReplicationContext(
                CreateActor(ownerConnectionId: 99),
                GameplayReplicationPolicy.AreaUnreliable(10f));

            int count = resolver.ResolveObservers(context, candidates, registry, results);

            Assert.AreEqual(0, count);
        }

        [Test]
        public void ObserverResolverDeduplicatesConnectionIdsAndHonorsResultBudget()
        {
            var resolver = new GameplayNetworkObserverResolver(
                initialCapacity: 2,
                maximumResultCount: 2);
            var duplicateFirst = new TestConnection(1);
            var duplicateSecond = new TestConnection(1);
            var second = new TestConnection(2);
            var overBudget = new TestConnection(3);
            var candidates = new INetConnection[]
            {
                duplicateFirst,
                duplicateSecond,
                second,
                overBudget
            };
            var results = new List<INetConnection>(2);
            var context = new GameplayReplicationContext(
                CreateActor(ownerConnectionId: 99),
                GameplayReplicationPolicy.AlwaysRelevantReliable);

            int count = resolver.ResolveObservers(
                context,
                candidates,
                observerSource: null,
                results: results);

            Assert.AreEqual(2, resolver.MaximumResultCount);
            Assert.AreEqual(2, count);
            Assert.AreSame(duplicateFirst, results[0]);
            Assert.AreSame(second, results[1]);
        }

        [Test]
        public void ObserverResolverRejectsWorkerThreadAccess()
        {
            var resolver = new GameplayNetworkObserverResolver();
            Exception captured = null;
            using var completed = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    resolver.ResolveObservers(
                        new GameplayReplicationContext(
                            CreateActor(ownerConnectionId: 1),
                            GameplayReplicationPolicy.AlwaysRelevantReliable),
                        Array.Empty<INetConnection>(),
                        observerSource: null,
                        results: new List<INetConnection>());
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
                finally
                {
                    completed.Set();
                }
            });

            thread.Start();
            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
            thread.Join();
            Assert.IsInstanceOf<InvalidOperationException>(captured);
        }

        [Test]
        public void ObserverResolver_ZeroRadiusDisablesAreaButNotTeamVisibility()
        {
            var resolver = new GameplayNetworkObserverResolver();
            var registry = new GameplayNetworkObserverRegistry();
            var connection = new TestConnection(2);
            var candidates = new INetConnection[] { connection };
            var results = new List<INetConnection>(1);
            registry.SetObserver(2, NetworkVector3.Zero, radius: 0f, teamId: 7);
            var actor = CreateActor(ownerConnectionId: 99, teamId: 7);

            int areaCount = resolver.ResolveObservers(
                new GameplayReplicationContext(actor, GameplayReplicationPolicy.AreaUnreliable(10f)),
                candidates,
                registry,
                results);
            int teamCount = resolver.ResolveObservers(
                new GameplayReplicationContext(actor, GameplayReplicationPolicy.TeamReliable),
                candidates,
                registry,
                results);

            Assert.AreEqual(0, areaCount);
            Assert.AreEqual(1, teamCount);
        }

        [Test]
        public void ObserverResolver_TeamPolicy_ReturnsSameTeamAndOwner()
        {
            var resolver = new GameplayNetworkObserverResolver();
            var registry = new GameplayNetworkObserverRegistry();
            var owner = new TestConnection(2);
            var sameTeam = new TestConnection(3);
            var otherTeam = new TestConnection(4);
            var candidates = new INetConnection[] { owner, sameTeam, otherTeam };
            var results = new List<INetConnection>(4);

            registry.SetObserver(2, NetworkVector3.Zero, 100f, teamId: 1);
            registry.SetObserver(3, NetworkVector3.Zero, 100f, teamId: 1);
            registry.SetObserver(4, NetworkVector3.Zero, 100f, teamId: 2);

            var actor = CreateActor(ownerConnectionId: 2, teamId: 1);
            var context = new GameplayReplicationContext(actor, GameplayReplicationPolicy.TeamReliable);

            int count = resolver.ResolveObservers(context, candidates, registry, results);

            Assert.AreEqual(2, count);
            Assert.AreSame(owner, results[0]);
            Assert.AreSame(sameTeam, results[1]);
        }

        [Test]
        public void ReplicationPolicyRejectsNonFiniteDistance()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GameplayReplicationPolicy.AreaUnreliable(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GameplayReplicationPolicy.AreaUnreliable(float.PositiveInfinity));
        }

        [Test]
        public void ObserverRegistryEnforcesConfiguredBudgetAndRejectsNonFiniteRadius()
        {
            var registry = new GameplayNetworkObserverRegistry(initialCapacity: 1, maximumObserverCount: 1);

            Assert.IsTrue(registry.TrySetObserver(1, NetworkVector3.Zero, 10f));
            Assert.IsFalse(registry.TrySetObserver(2, NetworkVector3.Zero, 10f));
            GameplayNetworkObserverAdmissionSnapshot snapshot = registry.GetAdmissionSnapshot();
            Assert.AreEqual(1, snapshot.ObserverCount);
            Assert.AreEqual(1, snapshot.MaximumObserverCount);
            Assert.AreEqual(1, snapshot.RejectedAdmissionCount);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                registry.TrySetObserver(1, NetworkVector3.Zero, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                registry.TrySetObserver(1, NetworkVector3.Zero, float.PositiveInfinity));
        }

        [Test]
        public void ObserverRegistryRejectsNonPositiveConnectionIdentifiers()
        {
            var registry = new GameplayNetworkObserverRegistry();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                registry.TrySetObserver(0, NetworkVector3.Zero, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                registry.TrySetObserver(-1, NetworkVector3.Zero, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                registry.TryGetObserver(0, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => registry.Remove(-1));
        }

        [Test]
        public void ObserverRegistryRejectsWorkerThreadAccess()
        {
            var registry = new GameplayNetworkObserverRegistry();
            Exception captured = null;
            using var completed = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    registry.TrySetObserver(1, NetworkVector3.Zero, 10f);
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
                finally
                {
                    completed.Set();
                }
            });

            thread.Start();
            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
            thread.Join();
            Assert.IsInstanceOf<InvalidOperationException>(captured);
        }

        [Test]
        public void Protocol_RegisterMessageCatalog_UsesGameplayFrameworkRange()
        {
            var catalog = new NetworkMessageCatalog();

            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.IsTrue(catalog.TryGet(
                GameplayFrameworkNetworkProtocol.MsgActorMigrationState,
                out NetworkMessageDescriptor descriptor));
            Assert.IsTrue(GameplayFrameworkNetworkProtocol.MessageRange.Contains(descriptor.MessageId));
            Assert.IsTrue(NetworkMessageRanges.Module.Contains(descriptor.MessageId));
            Assert.IsTrue(catalog.TryGetRegisteredRange(descriptor.MessageId, out NetworkMessageIdRange range));
            Assert.AreEqual(GameplayFrameworkNetworkProtocol.MessageOwner, range.Name);
            Assert.AreEqual(GameplayFrameworkNetworkProtocol.MessageOwner, descriptor.Owner);
            Assert.AreEqual("ActorMigrationState:v1", descriptor.ContractId);
            Assert.AreEqual(NetworkChannel.Reliable, descriptor.DefaultChannel);
        }

        [Test]
        public void Protocol_RegisterMessageCatalog_IsIdempotentForSameDescriptor()
        {
            var catalog = new NetworkMessageCatalog();

            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);
            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);

            // ActorMigrationState + DamageRequest + DamageResult are registered; re-registering is idempotent.
            Assert.AreEqual(3, catalog.MessageCount);
            Assert.AreEqual(1, catalog.ManifestCount);
        }

        [Test]
        public void ProtocolManifest_UsesFrozenV1SchemasAndFingerprint()
        {
            NetworkProtocolManifest manifest = GameplayFrameworkNetworkProtocol.CreateProtocolManifest();
            string[] canonicalSchemaLiterals =
            {
                "ActorMigrationState:v1",
                "DamageRequestMessage:v1",
                "DamageResultMessage:v1"
            };
            ulong[] expectedSchemaHashes =
            {
                0x06A6A8934573CD8EUL,
                0x43A411569257B773UL,
                0x937BD1B6AA2D5D2BUL
            };

            Assert.AreEqual(0x289A1A58AB6A7810UL, manifest.Fingerprint);
            Assert.AreEqual(manifest.Fingerprint, GameplayFrameworkNetworkProtocol.ProtocolFingerprint);
            Assert.AreEqual(
                ActorMigrationNetworkingExtensions.MaximumEncodedSize,
                manifest.Messages[0].MaxPayloadSize);
            Assert.AreEqual(
                GameplayFrameworkNetworkProtocol.DamageRequestPayloadBytes,
                manifest.Messages[1].MaxPayloadSize);
            Assert.AreEqual(
                GameplayFrameworkNetworkProtocol.DamageResultPayloadBytes,
                manifest.Messages[2].MaxPayloadSize);
            Assert.AreEqual(expectedSchemaHashes.Length, canonicalSchemaLiterals.Length);
            Assert.AreEqual(expectedSchemaHashes.Length, manifest.Messages.Count);
            for (int i = 0; i < expectedSchemaHashes.Length; i++)
            {
                Assert.AreEqual(
                    expectedSchemaHashes[i],
                    ComputeFnv1a64(canonicalSchemaLiterals[i]),
                    canonicalSchemaLiterals[i]);
                Assert.AreEqual(expectedSchemaHashes[i], manifest.Messages[i].SchemaHash);
                Assert.AreEqual(canonicalSchemaLiterals[i], manifest.Messages[i].ContractId);
            }
        }

        [Test]
        public void DamageWireSchemaFingerprint_IsFrozenFromLayoutAndResultCodeDescriptors()
        {
            string[] canonicalWireSchemas =
            {
                "DamageRequestMessage:v1|Sequence:u32le@0|InstigatorActorId:i32le@4|" +
                "TargetActorId:i32le@8|WeaponOrAbilityId:i32le@12|DamageEventType:u8@16|" +
                "RequestedDamage:f32le@17|ShotOrigin:f32le[3]@21|HitLocation:f32le[3]@33|" +
                "ClientTimeSeconds:f32le@45|size:49",
                "DamageResultMessage:v1|RequestSequence:u32le@0|InstigatorActorId:i32le@4|" +
                "TargetActorId:i32le@8|AppliedDamage:f32le@12|ResultCode:u8@16|" +
                "DamageEventType:u8@17|HitLocation:f32le[3]@18|size:30",
                "ServerDamageRejectReason:u8|Unknown=0|Accepted=1|InvalidPayload=2|" +
                "OwnershipMismatch=3|TargetNotDamageable=4|OutOfRange=5|OnCooldown=6|" +
                "TargetNotFound=7|Custom=8"
            };

            ulong fingerprint = ComputeWireSchemaFingerprint(canonicalWireSchemas);

            Assert.AreEqual(0x303A17781A25FAD4UL, fingerprint);
            Assert.AreEqual(fingerprint, GameplayFrameworkNetworkProtocol.DamageWireSchemaFingerprint);
            Assert.AreEqual(
                "303A17781A25FAD4",
                GameplayFrameworkNetworkProtocol.DefaultManifest.Metadata["damageWireSchemaFingerprint"]);
        }

        private static ulong ComputeFnv1a64(string canonicalLiteral)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;

            for (int i = 0; i < canonicalLiteral.Length; i++)
            {
                char character = canonicalLiteral[i];
                if (character > 0x7F)
                {
                    throw new AssertionException("Canonical schema literals must contain ASCII characters only.");
                }

                hash ^= (byte)character;
                hash = unchecked(hash * prime);
            }

            return hash;
        }

        private static ulong ComputeWireSchemaFingerprint(string[] canonicalSchemas)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;

            for (int schemaIndex = 0; schemaIndex < canonicalSchemas.Length; schemaIndex++)
            {
                string schema = canonicalSchemas[schemaIndex];
                for (int i = 0; i < schema.Length; i++)
                {
                    char character = schema[i];
                    if (character > 0x7F)
                    {
                        throw new AssertionException("Canonical wire schema descriptors must contain ASCII characters only.");
                    }

                    hash ^= (byte)character;
                    hash = unchecked(hash * prime);
                }

                if (schemaIndex + 1 < canonicalSchemas.Length)
                {
                    hash ^= 0xFF;
                    hash = unchecked(hash * prime);
                }
            }

            return hash;
        }

        [Test]
        public void Protocol_TryRegisterMessageCatalog_ReturnsFalseForMissingRuntimeContext()
        {
            Assert.IsFalse(GameplayFrameworkNetworkProtocol.TryRegisterMessageCatalog(null));
        }

        private static NetworkedGameplayActor CreateActor(int ownerConnectionId, int teamId = 0)
        {
            return new NetworkedGameplayActor(
                1,
                ownerConnectionId,
                0UL,
                teamId,
                uint.MaxValue,
                false,
                NetworkVector3.Zero);
        }

        private sealed class TestConnection : INetConnection
        {
            public TestConnection(int connectionId, string remoteAddress = "")
            {
                ConnectionId = connectionId;
                RemoteAddress = remoteAddress;
            }

            public int ConnectionId { get; }
            public string RemoteAddress { get; }
            public bool IsConnectedValue { get; set; } = true;
            public bool IsAuthenticatedValue { get; set; } = true;
            public bool IsConnected => IsConnectedValue;
            public bool IsAuthenticated => IsAuthenticatedValue;
            public int Ping => 0;
            public ConnectionQuality Quality => ConnectionQuality.Good;
            public double Jitter => 0d;
            public long BytesSent => 0L;
            public long BytesReceived => 0L;
            public ulong PlayerId { get; set; }

            public bool Equals(INetConnection other)
            {
                return other != null && other.ConnectionId == ConnectionId;
            }
        }

    }
}
