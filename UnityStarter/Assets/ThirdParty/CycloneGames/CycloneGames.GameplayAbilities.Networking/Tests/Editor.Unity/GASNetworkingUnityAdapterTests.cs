using NUnit.Framework;
using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Networking;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Networking;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.GameplayAbilities.Networking.Unity.Tests.Editor
{
    public sealed class GASNetworkingUnityAdapterTests
    {
        [TearDown]
        public void TearDown()
        {
            PoolManager.ClearAllPools();
            GASServices.Reset();
        }

        [Test]
        public void CaptureAndReplicatePendingStateDelta_NoObservers_DoesNotConsumePendingState()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var adapter = new GameplayAbilitiesNetworkedASCAdapter(asc, 77u, ownerConnectionId: 1);
            var network = new CapturingNetworkManager();
            var bridge = new NetworkedAbilityBridge(network, installSerializer: false, serializerOptions: null);

            asc.GrantAbility(new TestAbility());

            Assert.That(asc.PendingStateChangeMask.HasFlag(AbilitySystemStateChangeMask.GrantedAbilities), Is.True);

            var skipped = adapter.CaptureAndReplicatePendingStateDelta(bridge, Array.Empty<INetConnection>());

            Assert.That(skipped.StateDelta, Is.Null);
            Assert.That(network.BroadcastCount, Is.EqualTo(0));
            Assert.That(asc.PendingStateChangeMask.HasFlag(AbilitySystemStateChangeMask.GrantedAbilities), Is.True);

            var replicated = adapter.CaptureAndReplicatePendingStateDelta(
                bridge,
                new[] { new TestConnection(2) });

            Assert.That(replicated.StateDelta, Is.Not.Null);
            Assert.That(replicated.StateDelta.HasChanges, Is.True);
            Assert.That(replicated.StateDelta.ChangeMask.HasFlag(AbilitySystemStateChangeMask.GrantedAbilities), Is.True);
            Assert.That(asc.PendingStateChangeMask, Is.EqualTo(AbilitySystemStateChangeMask.None));
            Assert.That(network.BroadcastCount, Is.GreaterThanOrEqualTo(1));

            bridge.Dispose();
            adapter.Dispose();
            asc.Dispose();
        }

        [Test]
        public void OnAbilityEnded_ResolvesDuplicateDefinitionsBySpecHandle()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var ability = new TestAbility("SharedAbility");
            var registry = new DefaultGASNetIdRegistry();
            registry.RegisterAbilityDefinition(900, ability);
            var adapter = new GameplayAbilitiesNetworkedASCAdapter(asc, 77u, ownerConnectionId: 1, registry);

            var firstSpec = asc.GrantAbility(ability, level: 1, replicatedHandle: 101);
            var secondSpec = asc.GrantAbility(ability, level: 1, replicatedHandle: 202);
            int endedSpecHandle = 0;
            asc.OnAbilityEndedEvent += endedAbility => endedSpecHandle = endedAbility.Spec.Handle;

            Assert.That(asc.TryActivateAbility(secondSpec), Is.True);

            adapter.OnAbilityEnded(abilityDefinitionId: 900, abilitySpecHandle: secondSpec.Handle);

            Assert.That(endedSpecHandle, Is.EqualTo(secondSpec.Handle));
            Assert.That(secondSpec.IsActive, Is.False);
            Assert.That(firstSpec.IsActive, Is.False);

            adapter.Dispose();
            asc.Dispose();
        }

        [Test]
        public void FullStateReplication_PreservesDuplicateDefinitionsBySpecHandle()
        {
            var serverAsc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var clientAsc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var ability = new TestAbility("SharedAbility");
            var registry = new DefaultGASNetIdRegistry();
            registry.RegisterAbilityDefinition(900, ability);
            var serverAdapter = new GameplayAbilitiesNetworkedASCAdapter(serverAsc, 77u, ownerConnectionId: 1, registry);
            var clientAdapter = new GameplayAbilitiesNetworkedASCAdapter(clientAsc, 77u, ownerConnectionId: 1, registry);

            serverAsc.GrantAbility(ability, level: 1, replicatedHandle: 101);
            serverAsc.GrantAbility(ability, level: 2, replicatedHandle: 202);

            var state = serverAdapter.CaptureFullState();
            clientAdapter.OnFullState(state);
            var clientSpecs = clientAsc.GetActivatableAbilities();

            Assert.That(clientSpecs.Count, Is.EqualTo(2));
            Assert.That(ContainsSpecHandle(clientSpecs, 101), Is.True);
            Assert.That(ContainsSpecHandle(clientSpecs, 202), Is.True);

            clientAdapter.Dispose();
            serverAdapter.Dispose();
            clientAsc.Dispose();
            serverAsc.Dispose();
        }

        [Test]
        public void OnAbilityCancelled_ResolvesDynamicGrantRemovalByDefinitionIdFallback()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var removedAbility = new TestAbility("RemovedAbility");
            var targetAbility = new TestAbility("TargetAbility");
            var registry = new DefaultGASNetIdRegistry();
            registry.RegisterAbilityDefinition(100, removedAbility);
            registry.RegisterAbilityDefinition(777, targetAbility);
            var adapter = new GameplayAbilitiesNetworkedASCAdapter(asc, 77u, ownerConnectionId: 1, registry);

            var removedSpec = asc.GrantAbility(removedAbility);
            var targetSpec = asc.GrantAbility(targetAbility);
            asc.ClearAbility(removedSpec);
            var targetInstance = (TestAbility)targetSpec.GetPrimaryInstance();

            Assert.That(asc.TryActivateAbility(targetSpec), Is.True);

            adapter.OnAbilityCancelled(abilityDefinitionId: 777, abilitySpecHandle: 0);

            Assert.That(targetInstance.CancelCount, Is.EqualTo(1));
            Assert.That(targetSpec.IsActive, Is.False);

            adapter.Dispose();
            asc.Dispose();
        }

        private sealed class TestAbility : GameplayAbility
        {
            public TestAbility()
                : this("TestAbility")
            {
            }

            public TestAbility(string name)
            {
                Initialize(
                    name,
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    ENetExecutionPolicy.LocalOnly,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            public override GameplayAbility CreatePoolableInstance()
            {
                return new TestAbility(Name);
            }

            public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
            {
            }

            public override void CancelAbility()
            {
                CancelCount++;
                base.CancelAbility();
            }

            public int CancelCount { get; private set; }
        }

        private static bool ContainsSpecHandle(IReadOnlyList<GameplayAbilitySpec> specs, int handle)
        {
            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i].Handle == handle)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class CapturingNetworkManager : INetworkManager
        {
            public int BroadcastCount;

            public INetTransport Transport => null;
            public INetSerializer Serializer => null;

            public void RegisterHandler<T>(ushort msgId, Action<INetConnection, T> handler) where T : struct { }
            public void UnregisterHandler(ushort msgId) { }

            public NetworkSendResult SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
            {
                return NetworkSendResult.Accepted(0, 0);
            }

            public NetworkSendResult SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
            {
                return NetworkSendResult.Accepted(0, 0, connection);
            }

            public NetworkSendResult BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
            {
                BroadcastCount++;
                return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, 0, 0, 0);
            }

            public NetworkSendResult Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
            {
                BroadcastCount++;
                return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, 0, connections?.Count ?? 0, 0);
            }

            public void DisconnectClient(INetConnection connection) { }
        }

        private sealed class TestConnection : INetConnection
        {
            public TestConnection(int connectionId)
            {
                ConnectionId = connectionId;
            }

            public int ConnectionId { get; }
            public string RemoteAddress => "test";
            public bool IsConnected => true;
            public bool IsAuthenticated => true;
            public int Ping => 0;
            public ConnectionQuality Quality => ConnectionQuality.Excellent;
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
