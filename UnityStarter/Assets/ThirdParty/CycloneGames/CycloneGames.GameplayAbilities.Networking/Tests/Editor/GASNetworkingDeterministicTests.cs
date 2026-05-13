using NUnit.Framework;
using System;
using System.Collections.Generic;
using CycloneGames.Networking;
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
