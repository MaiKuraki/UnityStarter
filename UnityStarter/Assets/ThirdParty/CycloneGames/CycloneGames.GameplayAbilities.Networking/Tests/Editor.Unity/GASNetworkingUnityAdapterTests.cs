using NUnit.Framework;
using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Networking;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
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

        [Test]
        public void FullStateReplication_AppliesEffectsAttributesAndTagsThenRemovesStaleState()
        {
            GameplayTagManager.RegisterDynamicTag("Test.GAS.Networking.FullState.Powered", "Full state replication test tag");
            GameplayTagManager.InitializeIfNeeded();
            var tag = GameplayTagManager.RequestTag("Test.GAS.Networking.FullState.Powered");
            var effect = CreateHealthBuffEffect("NetworkFullStateBuff", 15f);
            var registry = new DefaultGASNetIdRegistry();
            registry.RegisterEffectDefinition(7001, effect);
            var serverAsc = CreateNetworkTestAsc(out var serverAttributes);
            var clientAsc = CreateNetworkTestAsc(out var clientAttributes);
            var serverAdapter = new GameplayAbilitiesNetworkedASCAdapter(serverAsc, 77u, ownerConnectionId: 1, registry);
            var clientAdapter = new GameplayAbilitiesNetworkedASCAdapter(clientAsc, 77u, ownerConnectionId: 1, registry);

            serverAttributes.SetBaseValue(serverAttributes.Health, GASFixedValue.FromInt(100));
            serverAsc.Tick(0f, true);
            serverAsc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, serverAsc));
            serverAsc.AddLooseGameplayTag(tag);
            serverAsc.Tick(0f, true);

            clientAdapter.OnFullState(serverAdapter.CaptureFullState());

            Assert.That(clientAsc.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(clientAsc.ActiveEffects[0].StackCount, Is.EqualTo(1));
            Assert.That(clientAttributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(clientAttributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(115).RawValue));
            Assert.That(clientAsc.HasMatchingGameplayTag(tag), Is.True);

            Assert.That(serverAsc.TryRemoveActiveEffect(serverAsc.ActiveEffects[0]), Is.True);
            serverAsc.RemoveLooseGameplayTag(tag);
            serverAsc.Tick(0f, true);

            clientAdapter.OnFullState(serverAdapter.CaptureFullState());

            Assert.That(clientAsc.ActiveEffects.Count, Is.EqualTo(0));
            Assert.That(clientAttributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(clientAttributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(clientAsc.HasMatchingGameplayTag(tag), Is.False);

            clientAdapter.Dispose();
            serverAdapter.Dispose();
            clientAsc.Dispose();
            serverAsc.Dispose();
        }

        [Test]
        public void PendingStateDelta_ClassifiesActiveEffectAddedStackUpdateAndRemoval()
        {
            var effect = CreateHealthBuffEffect("NetworkDeltaBuff", 10f);
            var registry = new DefaultGASNetIdRegistry();
            registry.RegisterEffectDefinition(7002, effect);
            var asc = CreateNetworkTestAsc(out var attributes);
            var adapter = new GameplayAbilitiesNetworkedASCAdapter(asc, 88u, ownerConnectionId: 1, registry);

            attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
            asc.Tick(0f, true);
            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.Tick(0f, true);

            var addedDelta = adapter.CapturePendingReplicatedStateDelta();

            Assert.That(addedDelta.AddedActiveEffectCount, Is.EqualTo(1));
            Assert.That(addedDelta.UpdatedActiveEffectCount, Is.EqualTo(0));
            Assert.That(addedDelta.StackChangedEffectCount, Is.EqualTo(0));
            Assert.That(addedDelta.RemovedEffectInstanceIdCount, Is.EqualTo(0));
            int effectInstanceId = addedDelta.AddedActiveEffects[0].EffectInstanceId;
            Assert.That(effectInstanceId, Is.Not.EqualTo(0));

            var activeEffect = asc.ActiveEffects[0];
            Assert.That(asc.TryApplyActiveEffectStackChange(activeEffect, 2), Is.True);

            var stackDelta = adapter.CapturePendingReplicatedStateDelta();

            Assert.That(stackDelta.AddedActiveEffectCount, Is.EqualTo(0));
            Assert.That(stackDelta.UpdatedActiveEffectCount, Is.EqualTo(0));
            Assert.That(stackDelta.StackChangedEffectCount, Is.EqualTo(1));
            Assert.That(stackDelta.StackChangedEffects[0].EffectInstanceId, Is.EqualTo(effectInstanceId));
            Assert.That(stackDelta.StackChangedEffects[0].NewStackCount, Is.EqualTo(2));

            Assert.That(asc.TryApplyReplicatedEffectUpdateRaw(
                activeEffect,
                level: 2,
                stackCount: 2,
                durationRaw: GASNetFixed.FromFloat(30f),
                timeRemainingRaw: GASNetFixed.FromFloat(12.5f),
                periodTimeRemainingRaw: 0L,
                setByCallerTags: Array.Empty<GameplayTag>(),
                setByCallerValuesRaw: Array.Empty<long>(),
                setByCallerCount: 0), Is.True);

            var updateDelta = adapter.CapturePendingReplicatedStateDelta();

            Assert.That(updateDelta.AddedActiveEffectCount, Is.EqualTo(0));
            Assert.That(updateDelta.StackChangedEffectCount, Is.EqualTo(0));
            Assert.That(updateDelta.UpdatedActiveEffectCount, Is.EqualTo(1));
            Assert.That(updateDelta.UpdatedActiveEffects[0].EffectInstanceId, Is.EqualTo(effectInstanceId));
            Assert.That(updateDelta.UpdatedActiveEffects[0].Level, Is.EqualTo(2));
            Assert.That(updateDelta.UpdatedActiveEffects[0].TimeRemainingRaw, Is.EqualTo(GASNetFixed.FromFloat(12.5f)));

            Assert.That(asc.TryRemoveActiveEffect(activeEffect), Is.True);

            var removeDelta = adapter.CapturePendingReplicatedStateDelta();

            Assert.That(removeDelta.AddedActiveEffectCount, Is.EqualTo(0));
            Assert.That(removeDelta.UpdatedActiveEffectCount, Is.EqualTo(0));
            Assert.That(removeDelta.StackChangedEffectCount, Is.EqualTo(0));
            Assert.That(removeDelta.RemovedEffectInstanceIdCount, Is.EqualTo(1));
            Assert.That(removeDelta.RemovedEffectInstanceIds[0], Is.EqualTo(effectInstanceId));

            adapter.Dispose();
            asc.Dispose();
        }

        private static AbilitySystemComponent CreateNetworkTestAsc(out NetworkTestAttributeSet attributes)
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory(), GASAbilitySystemRuntimeOptions.RuntimeOnly);
            attributes = new NetworkTestAttributeSet();
            asc.AddAttributeSet(attributes);
            return asc;
        }

        private static GameplayEffect CreateHealthBuffEffect(string name, float magnitude)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.HasDuration,
                30f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(magnitude))
                });
        }

        private sealed class NetworkTestAttributeSet : AttributeSet
        {
            public GameplayAttribute Health { get; } = new GameplayAttribute("Health");
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
