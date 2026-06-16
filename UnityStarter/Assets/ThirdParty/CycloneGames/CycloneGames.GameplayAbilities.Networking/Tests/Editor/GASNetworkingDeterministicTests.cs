using NUnit.Framework;
using System;
using System.Collections.Generic;
using CycloneGames.Networking;
using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Editor
{
    public sealed class GASNetworkingDeterministicTests
    {
        [Test]
        public void FixedConversion_UsesStableRawValue()
        {
            long rawA = GASNetFixed.FromFloat(12.5f);
            long rawB = GASNetFixed.FromFloat(12.5f);

            Assert.That(rawA, Is.EqualTo(rawB));
            Assert.That(GASNetFixed.ToFloat(rawA), Is.EqualTo(12.5f));
        }

        [Test]
        public void Checksum_UsesRawValues_NotFloatBits()
        {
            var attributes = new[]
            {
                new AttributeEntry
                {
                    AttributeId = 7,
                    BaseValueRaw = GASNetFixed.FromFloat(100f),
                    CurrentValueRaw = GASNetFixed.FromFloat(75.25f)
                }
            };

            uint checksumA = GASNetworkStateChecksum.Compute(
                null,
                0,
                null,
                0,
                attributes,
                attributes.Length,
                null,
                0);

            uint checksumB = GASNetworkStateChecksum.Compute(
                null,
                0,
                null,
                0,
                attributes,
                attributes.Length,
                null,
                0);

            Assert.That(checksumA, Is.Not.EqualTo(0u));
            Assert.That(checksumA, Is.EqualTo(checksumB));
        }

        [Test]
        public void Checksum_UsesRawEffectTimeAndSetByCallerValues()
        {
            var effectsA = new[]
            {
                new EffectReplicationData
                {
                    TargetNetworkId = 1u,
                    SourceNetworkId = 2u,
                    EffectInstanceId = 3,
                    EffectDefinitionId = 4,
                    Level = 1,
                    StackCount = 2,
                    DurationRaw = GASNetFixed.FromFloat(10f),
                    TimeRemainingRaw = GASNetFixed.FromFloat(7.5f),
                    PeriodTimeRemainingRaw = GASNetFixed.FromFloat(0.25f),
                    SetByCallerCount = 1,
                    SetByCallerEntries = new[]
                    {
                        new SetByCallerEntry
                        {
                            TagHash = 100,
                            ValueRaw = GASNetFixed.FromFloat(1.25f)
                        }
                    }
                }
            };

            var effectsB = new[]
            {
                effectsA[0]
            };

            uint checksumA = GASNetworkStateChecksum.Compute(null, 0, effectsA, effectsA.Length, null, 0, null, 0);
            uint checksumB = GASNetworkStateChecksum.Compute(null, 0, effectsB, effectsB.Length, null, 0, null, 0);

            effectsB[0].TimeRemainingRaw = GASNetFixed.FromFloat(7.25f);
            uint changedTimeChecksum = GASNetworkStateChecksum.Compute(null, 0, effectsB, effectsB.Length, null, 0, null, 0);

            effectsB[0] = effectsA[0];
            effectsB[0].SetByCallerEntries = new[]
            {
                new SetByCallerEntry
                {
                    TagHash = 100,
                    ValueRaw = GASNetFixed.FromFloat(1.5f)
                }
            };
            uint changedSetByCallerChecksum = GASNetworkStateChecksum.Compute(null, 0, effectsB, effectsB.Length, null, 0, null, 0);

            Assert.That(checksumA, Is.EqualTo(checksumB));
            Assert.That(changedTimeChecksum, Is.Not.EqualTo(checksumA));
            Assert.That(changedSetByCallerChecksum, Is.Not.EqualTo(checksumA));
        }

        [Test]
        public void Checksum_SortsTagHashes_ForContainerOrderIndependence()
        {
            int[] tagsA = { 30, 10, 20 };
            int[] tagsB = { 10, 20, 30 };

            uint checksumA = GASNetworkStateChecksum.Compute(null, 0, null, 0, null, 0, tagsA, tagsA.Length);
            uint checksumB = GASNetworkStateChecksum.Compute(null, 0, null, 0, null, 0, tagsB, tagsB.Length);

            Assert.That(checksumA, Is.EqualTo(checksumB));
        }

        [Test]
        public void AttributeSyncManager_FlushDirty_UsesRawAttributeValues()
        {
            var network = new CapturingNetworkManager();
            var bridge = new NetworkedAbilityBridge(network);
            var manager = new AttributeSyncManager(bridge);
            var owner = new TestConnection(10);
            long baseRaw = GASNetFixed.FromFloat(100.25f);
            long currentRaw = GASNetFixed.FromFloat(77.5f);

            manager.MarkDirtyRaw(5u, 9, baseRaw, currentRaw);
            manager.FlushDirty(_ => owner.ConnectionId, _ => Array.Empty<INetConnection>(), id => id == owner.ConnectionId ? owner : null);

            Assert.That(network.LastAttributeData.AttributeCount, Is.EqualTo(1));
            Assert.That(network.LastAttributeData.Attributes[0].AttributeId, Is.EqualTo(9));
            Assert.That(network.LastAttributeData.Attributes[0].BaseValueRaw, Is.EqualTo(baseRaw));
            Assert.That(network.LastAttributeData.Attributes[0].CurrentValueRaw, Is.EqualTo(currentRaw));
        }

        [Test]
        public void AttributeSyncManager_SendFullSyncRaw_FiltersPrivateAttributesForObservers()
        {
            var network = new CapturingNetworkManager();
            var bridge = new NetworkedAbilityBridge(network);
            var manager = new AttributeSyncManager(bridge);
            var observer = new TestConnection(20);
            var entries = new[]
            {
                new AttributeEntry { AttributeId = 1, BaseValueRaw = 100L, CurrentValueRaw = 80L },
                new AttributeEntry { AttributeId = 2, BaseValueRaw = 200L, CurrentValueRaw = 150L }
            };

            manager.RegisterPublicAttribute(2);
            manager.SendFullSyncRaw(observer, 7u, entries, isOwner: false);

            Assert.That(network.LastAttributeData.IsFullSync, Is.True);
            Assert.That(network.LastAttributeData.AttributeCount, Is.EqualTo(1));
            Assert.That(network.LastAttributeData.Attributes[0].AttributeId, Is.EqualTo(2));
            Assert.That(network.LastAttributeData.Attributes[0].CurrentValueRaw, Is.EqualTo(150L));
        }

        [Test]
        public void GASNetworkSerializer_AttributeUpdate_UsesCountAsWireBoundary()
        {
            var serializer = new GASNetworkSerializer(new ThrowingSerializer());
            var source = new AttributeUpdateData
            {
                TargetNetworkId = 9u,
                IsFullSync = true,
                AttributeCount = 1,
                Attributes = new[]
                {
                    new AttributeEntry { AttributeId = 7, BaseValueRaw = 100L, CurrentValueRaw = 80L },
                    new AttributeEntry { AttributeId = 99, BaseValueRaw = 999L, CurrentValueRaw = 999L }
                }
            };

            byte[] buffer = new byte[256];
            serializer.Serialize(source, buffer, 0, out int written);
            var decoded = serializer.Deserialize<AttributeUpdateData>(new ReadOnlySpan<byte>(buffer, 0, written));

            Assert.That(decoded.TargetNetworkId, Is.EqualTo(9u));
            Assert.That(decoded.IsFullSync, Is.True);
            Assert.That(decoded.AttributeCount, Is.EqualTo(1));
            Assert.That(decoded.Attributes.Length, Is.EqualTo(1));
            Assert.That(decoded.Attributes[0].AttributeId, Is.EqualTo(7));
        }

        [Test]
        public void GASNetworkSerializer_RejectsCountsAboveConfiguredLimit()
        {
            var serializer = new GASNetworkSerializer(
                new ThrowingSerializer(),
                new GASNetworkSerializerOptions { MaxAttributes = 1 });
            var source = new AttributeUpdateData
            {
                TargetNetworkId = 9u,
                AttributeCount = 2,
                Attributes = new[]
                {
                    new AttributeEntry { AttributeId = 1 },
                    new AttributeEntry { AttributeId = 2 }
                }
            };

            Assert.Throws<InvalidOperationException>(() =>
                serializer.Serialize(source, new byte[256], 0, out _));
        }

        [Test]
        public void GASNetworkSerializerOptions_Default_ReturnsIsolatedInstances()
        {
            var first = GASNetworkSerializerOptions.Default;
            first.MaxAttributes = 1;

            var second = GASNetworkSerializerOptions.Default;

            Assert.That(second.MaxAttributes, Is.EqualTo(512));
        }

        [Test]
        public void NetworkedAbilityBridge_InstallsConfiguredSerializerOptions()
        {
            var options = GASNetworkSerializerOptions.CreateConservative();
            options.MaxAttributes = 3;
            var network = new ConfigurableNetworkManager(new ThrowingSerializer());

            _ = new NetworkedAbilityBridge(network, options);
            options.MaxAttributes = 99;

            var serializer = network.Serializer as GASNetworkSerializer;
            Assert.That(serializer, Is.Not.Null);
            Assert.That(serializer.Options.MaxAttributes, Is.EqualTo(3));
        }

        [Test]
        public void GASNetworkSerializer_Deserialize_RejectsNegativeCounts()
        {
            var serializer = new GASNetworkSerializer(new ThrowingSerializer());

            using var writer = NetworkBufferPool.Get();
            writer.WriteUInt(9u);
            writer.WriteByte(1);
            writer.WriteInt(-1);
            ArraySegment<byte> payload = writer.ToArraySegment();

            Assert.Throws<InvalidOperationException>(() =>
                serializer.Deserialize<AttributeUpdateData>(
                    new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count)));
        }

        [Test]
        public void GASNetworkSerializer_Deserialize_RejectsTruncatedPayload()
        {
            var serializer = new GASNetworkSerializer(new ThrowingSerializer());

            using var writer = NetworkBufferPool.Get();
            writer.WriteUInt(9u);
            writer.WriteByte(1);
            writer.WriteInt(1);
            writer.WriteInt(7);
            ArraySegment<byte> payload = writer.ToArraySegment();

            Assert.Throws<InvalidOperationException>(() =>
                serializer.Deserialize<AttributeUpdateData>(
                    new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count)));
        }

        [Test]
        public void NetworkedAbilityBridge_FullStateRequest_UsesConnectionScopedState()
        {
            var network = new FullStateCapturingNetworkManager();
            var bridge = new NetworkedAbilityBridge(network, installSerializer: false, serializerOptions: null);
            var asc = new ScopedFullStateASC(44u, ownerConnectionId: 10);
            var observer = new TestConnection(20);

            bridge.RegisterASC(asc.NetworkId, asc.OwnerConnectionId, asc);
            bridge.FullStateRequestAuthorizer = (_, _) => true;
            bridge.RegisterHandlers();

            network.DeliverFullStateRequest(observer, new FullStateRequest { TargetNetworkId = asc.NetworkId });

            Assert.That(network.LastFullState.TargetNetworkId, Is.EqualTo(44u));
            Assert.That(network.LastFullState.AttributeCount, Is.EqualTo(1));
            Assert.That(network.LastFullState.Attributes[0].AttributeId, Is.EqualTo(20));
        }

        private sealed class CapturingNetworkManager : INetworkManager
        {
            public AttributeUpdateData LastAttributeData;
            public INetTransport Transport => null;
            public INetSerializer Serializer => null;

            public void RegisterHandler<T>(ushort msgId, Action<INetConnection, T> handler) where T : struct { }
            public void UnregisterHandler(ushort msgId) { }
            public void SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }

            public void SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
            {
                if (message is AttributeUpdateData data)
                {
                    LastAttributeData = data;
                }
            }

            public void BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }
            public void Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }
            public void DisconnectClient(INetConnection connection) { }
        }

        private sealed class ThrowingSerializer : INetSerializer
        {
            public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
            {
                throw new InvalidOperationException("Fallback serializer should not be used by this test.");
            }

            public void Serialize<T>(in T value, INetWriter writer) where T : struct
            {
                throw new InvalidOperationException("Fallback serializer should not be used by this test.");
            }

            public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct
            {
                throw new InvalidOperationException("Fallback serializer should not be used by this test.");
            }

            public T Deserialize<T>(INetReader reader) where T : struct
            {
                throw new InvalidOperationException("Fallback serializer should not be used by this test.");
            }
        }

        private sealed class ConfigurableNetworkManager : INetworkManager, INetworkSerializerConfigurable
        {
            public ConfigurableNetworkManager(INetSerializer serializer)
            {
                Serializer = serializer;
            }

            public INetTransport Transport => null;
            public INetSerializer Serializer { get; private set; }

            public void SetSerializer(INetSerializer serializer)
            {
                Serializer = serializer;
            }

            public void RegisterHandler<T>(ushort msgId, Action<INetConnection, T> handler) where T : struct { }
            public void UnregisterHandler(ushort msgId) { }
            public void SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }
            public void SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }
            public void BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }
            public void Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }
            public void DisconnectClient(INetConnection connection) { }
        }

        private sealed class FullStateCapturingNetworkManager : INetworkManager
        {
            private Action<INetConnection, FullStateRequest> _fullStateHandler;

            public GASFullStateData LastFullState;
            public INetTransport Transport => null;
            public INetSerializer Serializer => null;

            public void RegisterHandler<T>(ushort msgId, Action<INetConnection, T> handler) where T : struct
            {
                if (msgId == NetworkedAbilityBridge.MsgFullStateRequest && typeof(T) == typeof(FullStateRequest))
                    _fullStateHandler = (conn, message) => handler(conn, (T)(object)message);
            }

            public void UnregisterHandler(ushort msgId) { }
            public void SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }

            public void SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
            {
                if (msgId == NetworkedAbilityBridge.MsgFullState && message is GASFullStateData data)
                    LastFullState = data;
            }

            public void BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }
            public void Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }
            public void DisconnectClient(INetConnection connection) { }

            public void DeliverFullStateRequest(INetConnection sender, FullStateRequest request)
            {
                _fullStateHandler?.Invoke(sender, request);
            }
        }

        private sealed class ScopedFullStateASC : INetworkedASC, INetworkedASCConnectionScopedFullState
        {
            public ScopedFullStateASC(uint networkId, int ownerConnectionId)
            {
                NetworkId = networkId;
                OwnerConnectionId = ownerConnectionId;
            }

            public uint NetworkId { get; }
            public int OwnerConnectionId { get; }

            public GASFullStateData CaptureFullState()
            {
                return BuildFullState(OwnerConnectionId);
            }

            public GASFullStateData CaptureFullStateForConnection(INetConnection client)
            {
                return BuildFullState(client.ConnectionId);
            }

            private GASFullStateData BuildFullState(int attributeId)
            {
                return new GASFullStateData
                {
                    TargetNetworkId = NetworkId,
                    AttributeCount = 1,
                    Attributes = new[]
                    {
                        new AttributeEntry
                        {
                            AttributeId = attributeId,
                            BaseValueRaw = 1L,
                            CurrentValueRaw = 2L
                        }
                    }
                };
            }

            public void OnServerConfirmActivation(int abilityIndex, int predictionKey) { }
            public void OnServerConfirmActivation(int abilityIndex, int predictionKey, int predictionKeyOwner, int predictionInputSequence) { }
            public void OnServerRejectActivation(int abilityIndex, int predictionKey) { }
            public void OnServerRejectActivation(int abilityIndex, int predictionKey, int predictionKeyOwner, int predictionInputSequence) { }
            public void OnAbilityEnded(int abilityIndex) { }
            public void OnAbilityCancelled(int abilityIndex) { }
            public void OnAbilityMulticast(AbilityMulticastData data) { }
            public void OnReplicatedEffectApplied(EffectReplicationData data) { }
            public void OnReplicatedEffectRemoved(int effectInstanceId) { }
            public void OnReplicatedStackChanged(int effectInstanceId, int newStackCount) { }
            public void OnReplicatedEffectUpdated(EffectUpdateData data) { }
            public void OnReplicatedAttributeUpdate(AttributeUpdateData data) { }
            public void OnReplicatedTagUpdate(TagUpdateData data) { }
            public void OnFullState(GASFullStateData data) { }
            public bool OnStateSyncMetadata(GASStateSyncMetadata metadata) => true;
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
