using System;
using System.Collections.Generic;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.Networking;
using CycloneGames.Networking.Replication;
using NUnit.Framework;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Networking.Tests.Editor
{
    public sealed class AIPerceptionNetworkingIntegrationTests
    {
        [Test]
        public void Protocol_RegisterMessageCatalog_UsesExactV1Budgets()
        {
            var catalog = new NetworkMessageCatalog();

            AIPerceptionNetworkProtocol.RegisterMessageCatalog(catalog);

            AssertDescriptor(
                catalog,
                AIPerceptionNetworkProtocol.MSG_MANIFEST_HANDSHAKE,
                "AIPerceptionManifestHandshakeMessage:v1",
                NetworkChannel.Reliable,
                AIPerceptionNetworkWireCodec.HandshakePayloadBytes);
            AssertDescriptor(
                catalog,
                AIPerceptionNetworkProtocol.MSG_DETECTION_EVENT,
                "AIPerceptionDetectionEventMessage:v1",
                NetworkChannel.UnreliableSequenced,
                AIPerceptionNetworkWireCodec.DetectionEventPayloadBytes);
            AssertDescriptor(
                catalog,
                AIPerceptionNetworkProtocol.MSG_DETECTION_SNAPSHOT,
                "AIPerceptionDetectionSnapshotMessage:v1",
                NetworkChannel.UnreliableSequenced,
                AIPerceptionNetworkProtocol.DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE);
            AssertDescriptor(
                catalog,
                AIPerceptionNetworkProtocol.MSG_MEMORY_SNAPSHOT,
                "AIPerceptionMemorySnapshotMessage:v1",
                NetworkChannel.Reliable,
                AIPerceptionNetworkProtocol.DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE);
            AssertDescriptor(
                catalog,
                AIPerceptionNetworkProtocol.MSG_AUTHORITY_TRANSFER,
                "AIPerceptionAuthorityTransferMessage:v1",
                NetworkChannel.Reliable,
                AIPerceptionNetworkWireCodec.AuthorityTransferPayloadBytes);
            AssertDescriptor(
                catalog,
                AIPerceptionNetworkProtocol.MSG_FULL_STATE_REQUEST,
                "AIPerceptionFullStateRequestMessage:v1",
                NetworkChannel.Reliable,
                AIPerceptionNetworkWireCodec.FullStateRequestPayloadBytes);
            Assert.That(catalog.MessageCount, Is.EqualTo(6));
            Assert.That(AIPerceptionNetworkProtocol.MAX_SNAPSHOT_ENTRIES, Is.EqualTo(125));
        }

        [Test]
        public void ProtocolManifest_FreezesV1ContractHashesAndFingerprint()
        {
            NetworkProtocolManifest manifest = AIPerceptionNetworkProtocol.CreateProtocolManifest();
            ulong[] expectedSchemaHashes =
            {
                0xE24FD3DF9C74AB1CUL,
                0x7FB1540691D2B0BFUL,
                0xA9F15D28F3BC339DUL,
                0xE163CF3EDDDC2E25UL,
                0xDD0A7C2010BB2D4CUL,
                0xF715DC535205849DUL
            };

            Assert.That(manifest.Fingerprint, Is.EqualTo(0x574265EF59340254UL));
            Assert.That(AIPerceptionNetworkProtocol.ProtocolFingerprint, Is.EqualTo(manifest.Fingerprint));
            Assert.That(manifest.Messages.Count, Is.EqualTo(expectedSchemaHashes.Length));
            for (int i = 0; i < expectedSchemaHashes.Length; i++)
            {
                Assert.That(manifest.Messages[i].SchemaHash, Is.EqualTo(expectedSchemaHashes[i]));
            }
        }

        [Test]
        public void HandshakeCodec_WritesFrozenLittleEndianGoldenBytes()
        {
            var message = new AIPerceptionManifestHandshakeMessage(
                0x0807060504030201UL,
                0x1817161514131211UL,
                AIPerceptionNetworkFeatureFlags.DetectionEvents |
                AIPerceptionNetworkFeatureFlags.DetectionSnapshots,
                AIPerceptionNetworkFeatureFlags.DetectionSnapshots,
                1,
                1);
            byte[] payload = new byte[AIPerceptionNetworkWireCodec.HandshakePayloadBytes];
            byte[] expected =
            {
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
                0x03, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x01, 0x01
            };

            AIPerceptionNetworkWireCodecResult writeResult = AIPerceptionNetworkWireCodec.TryWriteHandshake(
                in message,
                payload,
                out int bytesWritten);
            AIPerceptionNetworkWireCodecResult readResult = AIPerceptionNetworkWireCodec.TryReadHandshake(
                payload,
                out AIPerceptionManifestHandshakeMessage decoded);

            Assert.That(writeResult, Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(bytesWritten, Is.EqualTo(expected.Length));
            CollectionAssert.AreEqual(expected, payload);
            Assert.That(readResult, Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(decoded.ProtocolFingerprint, Is.EqualTo(message.ProtocolFingerprint));
            Assert.That(decoded.PerceptionProfileHash, Is.EqualTo(message.PerceptionProfileHash));
            Assert.That(decoded.SupportedFeatures, Is.EqualTo(message.SupportedFeatures));
            Assert.That(decoded.RequiredFeatures, Is.EqualTo(message.RequiredFeatures));

            byte[] unknownFeatures = (byte[])payload.Clone();
            unknownFeatures[19] = 0x80;
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadHandshake(unknownFeatures, out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.MalformedMessage));
        }

        [Test]
        public void FixedMessages_RoundTripWithoutSerializerDiscovery()
        {
            AIPerceptionDetectionEntry entry = CreateEntry(10u, AIPerceptionNetworkSensorKind.Sight, tick: 40);
            var detectionEvent = new AIPerceptionDetectionEventMessage(
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                99u,
                7,
                40,
                AIPerceptionNetworkEventKind.Updated,
                3u,
                AIPerceptionNetworkHash.Compute(in entry),
                in entry);
            byte[] eventPayload = new byte[AIPerceptionNetworkWireCodec.DetectionEventPayloadBytes];

            Assert.That(
                AIPerceptionNetworkWireCodec.TryWriteDetectionEvent(
                    in detectionEvent,
                    eventPayload,
                    out int eventBytes),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(eventBytes, Is.EqualTo(eventPayload.Length));
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionEvent(
                    eventPayload,
                    out AIPerceptionDetectionEventMessage decodedEvent),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(decodedEvent.ObserverNetworkId, Is.EqualTo(detectionEvent.ObserverNetworkId));
            Assert.That(decodedEvent.AuthorityGeneration, Is.EqualTo(3u));
            AssertEntryEqual(in entry, in decodedEvent.Entry);

            var transfer = new AIPerceptionAuthorityTransferMessage(
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                99u,
                1,
                2,
                100UL,
                200UL,
                4u,
                8,
                41,
                0xAABBCCDDEEFF0011UL);
            byte[] transferPayload = new byte[AIPerceptionNetworkWireCodec.AuthorityTransferPayloadBytes];
            Assert.That(
                AIPerceptionNetworkWireCodec.TryWriteAuthorityTransfer(
                    in transfer,
                    transferPayload,
                    out int transferBytes),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(transferBytes, Is.EqualTo(transferPayload.Length));
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadAuthorityTransfer(
                    transferPayload,
                    out AIPerceptionAuthorityTransferMessage decodedTransfer),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(decodedTransfer.AuthorityGeneration, Is.EqualTo(4u));
            Assert.That(decodedTransfer.SnapshotStateHash, Is.EqualTo(transfer.SnapshotStateHash));

            var request = new AIPerceptionFullStateRequestMessage(
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                99u,
                9,
                42,
                AIPerceptionNetworkSensorKind.Any,
                4u,
                0x1122334455667788UL);
            byte[] requestPayload = new byte[AIPerceptionNetworkWireCodec.FullStateRequestPayloadBytes];
            Assert.That(
                AIPerceptionNetworkWireCodec.TryWriteFullStateRequest(
                    in request,
                    requestPayload,
                    out int requestBytes),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(requestBytes, Is.EqualTo(requestPayload.Length));
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadFullStateRequest(
                    requestPayload,
                    out AIPerceptionFullStateRequestMessage decodedRequest),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(decodedRequest.ExpectedAuthorityGeneration, Is.EqualTo(4u));
            Assert.That(decodedRequest.LastKnownStateHash, Is.EqualTo(request.LastKnownStateHash));
        }

        [Test]
        public void SnapshotCodec_RoundTripsIntoCallerOwnedEntries()
        {
            AIPerceptionDetectionEntry[] source =
            {
                CreateEntry(10u, AIPerceptionNetworkSensorKind.Sight, tick: 50),
                CreateEntry(20u, AIPerceptionNetworkSensorKind.Hearing, tick: 49)
            };
            var message = new AIPerceptionDetectionSnapshotMessage(
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                77u,
                12,
                50,
                AIPerceptionNetworkSensorKind.Any,
                6u,
                (ushort)source.Length,
                AIPerceptionNetworkHash.Compute(source));
            byte[] payload = new byte[AIPerceptionNetworkWireCodec.GetSnapshotPayloadBytes(source.Length)];
            var destination = new AIPerceptionDetectionEntry[source.Length];

            Assert.That(
                AIPerceptionNetworkWireCodec.TryWriteDetectionSnapshot(
                    in message,
                    source,
                    payload,
                    out int bytesWritten),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(bytesWritten, Is.EqualTo(payload.Length));
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionSnapshot(
                    payload,
                    destination,
                    out AIPerceptionDetectionSnapshotMessage decoded,
                    out int entryCount),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(entryCount, Is.EqualTo(source.Length));
            Assert.That(decoded.EntryCount, Is.EqualTo(source.Length));
            Assert.That(decoded.StateHash, Is.EqualTo(message.StateHash));
            AssertEntryEqual(in source[0], in destination[0]);
            AssertEntryEqual(in source[1], in destination[1]);

            var mismatchedSensor = new AIPerceptionDetectionSnapshotMessage(
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                77u,
                12,
                50,
                AIPerceptionNetworkSensorKind.Sight,
                6u,
                (ushort)source.Length,
                AIPerceptionNetworkHash.Compute(source));
            Assert.That(
                AIPerceptionNetworkWireCodec.TryWriteDetectionSnapshot(
                    in mismatchedSensor,
                    source,
                    payload,
                    out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.MalformedMessage));
        }

        [Test]
        public void SnapshotDecode_RejectsTamperedConcreteSensorHeaderWithValidEntryHash()
        {
            AIPerceptionDetectionEntry[] source =
            {
                CreateEntry(10u, AIPerceptionNetworkSensorKind.Sight, tick: 50),
                CreateEntry(20u, AIPerceptionNetworkSensorKind.Hearing, tick: 50)
            };
            var message = new AIPerceptionDetectionSnapshotMessage(
                1,
                77u,
                12,
                50,
                AIPerceptionNetworkSensorKind.Any,
                6u,
                (ushort)source.Length,
                AIPerceptionNetworkHash.Compute(source));
            byte[] payload = new byte[AIPerceptionNetworkWireCodec.GetSnapshotPayloadBytes(source.Length)];
            Assert.That(
                AIPerceptionNetworkWireCodec.TryWriteDetectionSnapshot(
                    in message,
                    source,
                    payload,
                    out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));

            // StateHash covers the canonical entries, so changing only the header preserves a valid entry hash.
            payload[11] = (byte)AIPerceptionNetworkSensorKind.Sight;

            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionSnapshot(
                    payload,
                    new AIPerceptionDetectionEntry[source.Length],
                    out _,
                    out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.MalformedMessage));
        }

        [Test]
        public void EmptySnapshot_UsesFNVOffsetAsAuthoritativeEmptySetHash()
        {
            ReadOnlySpan<AIPerceptionDetectionEntry> noEntries =
                ReadOnlySpan<AIPerceptionDetectionEntry>.Empty;
            ulong emptyHash = AIPerceptionNetworkHash.Compute(noEntries);
            var message = new AIPerceptionDetectionSnapshotMessage(
                1,
                7u,
                1,
                10,
                AIPerceptionNetworkSensorKind.Any,
                1u,
                0,
                emptyHash);
            byte[] payload = new byte[AIPerceptionNetworkWireCodec.DetectionSnapshotHeaderBytes];

            Assert.That(emptyHash, Is.EqualTo(14695981039346656037UL));
            Assert.That(
                AIPerceptionNetworkWireCodec.TryWriteDetectionSnapshot(
                    in message,
                    noEntries,
                    payload,
                    out int bytesWritten),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(bytesWritten, Is.EqualTo(AIPerceptionNetworkWireCodec.DetectionSnapshotHeaderBytes));
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionSnapshot(
                    payload,
                    Span<AIPerceptionDetectionEntry>.Empty,
                    out AIPerceptionDetectionSnapshotMessage decoded,
                    out int decodedCount),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));
            Assert.That(decodedCount, Is.Zero);
            Assert.That(decoded.StateHash, Is.EqualTo(emptyHash));
        }

        [Test]
        public void ClearedEvent_IsPerTargetAndStillRequiresValidEntry()
        {
            AIPerceptionDetectionEntry entry = CreateEntry(
                10u,
                AIPerceptionNetworkSensorKind.Sight,
                tick: 10);
            var perTargetClear = new AIPerceptionDetectionEventMessage(
                1,
                5u,
                2,
                10,
                AIPerceptionNetworkEventKind.Cleared,
                1u,
                AIPerceptionNetworkHash.Compute(in entry),
                in entry);
            var invalidGlobalClear = new AIPerceptionDetectionEventMessage(
                1,
                5u,
                2,
                10,
                AIPerceptionNetworkEventKind.Cleared,
                1u,
                14695981039346656037UL,
                default);

            Assert.That(perTargetClear.IsValid, Is.True);
            Assert.That(invalidGlobalClear.IsValid, Is.False);
        }

        [Test]
        public void SnapshotCodec_RejectsCapacityLengthHashOrderAndNonCanonicalFloat()
        {
            AIPerceptionDetectionEntry first = CreateEntry(10u, AIPerceptionNetworkSensorKind.Sight, tick: 50);
            AIPerceptionDetectionEntry second = CreateEntry(20u, AIPerceptionNetworkSensorKind.Hearing, tick: 50);
            AIPerceptionDetectionEntry[] canonical = { first, second };
            var message = new AIPerceptionDetectionSnapshotMessage(
                1,
                77u,
                12,
                50,
                AIPerceptionNetworkSensorKind.Any,
                6u,
                2,
                AIPerceptionNetworkHash.Compute(canonical));
            byte[] payload = new byte[AIPerceptionNetworkWireCodec.GetSnapshotPayloadBytes(2)];
            Assert.That(
                AIPerceptionNetworkWireCodec.TryWriteDetectionSnapshot(in message, canonical, payload, out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));

            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionSnapshot(
                    payload,
                    new AIPerceptionDetectionEntry[1],
                    out _,
                    out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.DestinationEntryCapacityTooSmall));
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionSnapshot(
                    payload.AsSpan(0, payload.Length - 1),
                    new AIPerceptionDetectionEntry[2],
                    out _,
                    out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.InvalidPayloadLength));

            byte[] excessiveCount = (byte[])payload.Clone();
            excessiveCount[16] = 126;
            excessiveCount[17] = 0;
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionSnapshot(
                    excessiveCount,
                    new AIPerceptionDetectionEntry[2],
                    out _,
                    out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.MalformedMessage));

            byte[] corruptedHash = (byte[])payload.Clone();
            corruptedHash[18] ^= 0x01;
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionSnapshot(
                    corruptedHash,
                    new AIPerceptionDetectionEntry[2],
                    out _,
                    out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.MalformedMessage));

            AIPerceptionDetectionEntry[] reversed = { second, first };
            var reversedHeader = new AIPerceptionDetectionSnapshotMessage(
                1,
                77u,
                12,
                50,
                AIPerceptionNetworkSensorKind.Any,
                6u,
                2,
                AIPerceptionNetworkHash.Compute(reversed));
            Assert.That(
                AIPerceptionNetworkWireCodec.TryWriteDetectionSnapshot(
                    in reversedHeader,
                    reversed,
                    payload,
                    out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.MalformedMessage));

            var negativeZero = new AIPerceptionDetectionEntry(
                30u,
                1,
                AIPerceptionNetworkSensorKind.Sight,
                AIPerceptionDetectionFlags.None,
                NetworkVector3.Zero,
                BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
                1f,
                50,
                0);
            Assert.That(
                AIPerceptionNetworkMessageValidator.Validate(in negativeZero),
                Is.EqualTo(AIPerceptionNetworkMessageValidationResult.NonCanonicalValue));
        }

        [Test]
        public void EventCodec_RejectsUnknownEnumAndNonFinitePayload()
        {
            AIPerceptionDetectionEntry entry = CreateEntry(10u, AIPerceptionNetworkSensorKind.Sight, tick: 10);
            var message = new AIPerceptionDetectionEventMessage(
                1,
                5u,
                2,
                10,
                AIPerceptionNetworkEventKind.Detected,
                1u,
                AIPerceptionNetworkHash.Compute(in entry),
                in entry);
            byte[] payload = new byte[AIPerceptionNetworkWireCodec.DetectionEventPayloadBytes];
            Assert.That(
                AIPerceptionNetworkWireCodec.TryWriteDetectionEvent(in message, payload, out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.Success));

            byte[] unknownEvent = (byte[])payload.Clone();
            unknownEvent[11] = 0xFE;
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionEvent(unknownEvent, out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.MalformedMessage));

            byte[] unknownFlags = (byte[])payload.Clone();
            unknownFlags[33] = 0x80;
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionEvent(unknownFlags, out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.MalformedMessage));

            byte[] nanDistance = (byte[])payload.Clone();
            nanDistance[46] = 0x00;
            nanDistance[47] = 0x00;
            nanDistance[48] = 0xC0;
            nanDistance[49] = 0x7F;
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionEvent(nanDistance, out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.MalformedMessage));

            byte[] invalidVisibility = (byte[])payload.Clone();
            invalidVisibility[50] = 0x00;
            invalidVisibility[51] = 0x00;
            invalidVisibility[52] = 0x00;
            invalidVisibility[53] = 0x40;
            Assert.That(
                AIPerceptionNetworkWireCodec.TryReadDetectionEvent(invalidVisibility, out _),
                Is.EqualTo(AIPerceptionNetworkWireCodecResult.MalformedMessage));

            var negativeTypeId = new AIPerceptionDetectionEntry(
                10u,
                -1,
                AIPerceptionNetworkSensorKind.Sight,
                AIPerceptionDetectionFlags.None,
                NetworkVector3.Zero,
                1f,
                1f,
                10,
                0);
            Assert.That(
                AIPerceptionNetworkMessageValidator.Validate(in negativeTypeId),
                Is.EqualTo(AIPerceptionNetworkMessageValidationResult.ValueOutOfRange));
        }

        [Test]
        public void ProfileHashAndHandshake_RejectConfigurationAndFeatureDrift()
        {
            AIPerceptionNetworkProfile local = AIPerceptionNetworkProfiles.ServerAuthoritative;
            AIPerceptionManifestHandshakeMessage handshake =
                AIPerceptionManifestHandshakeMessage.CreateLocal(local);
            AIPerceptionNetworkProfile drifted = AIPerceptionNetworkProfiles
                .CreateServerAuthoritativeBuilder()
                .Build();
            Assert.That(local.ProfileHash, Is.EqualTo(drifted.ProfileHash));
            var changedBuilder = AIPerceptionNetworkProfiles.CreateServerAuthoritativeBuilder();
            changedBuilder.SnapshotIntervalTicks++;
            AIPerceptionNetworkProfile changed = changedBuilder.Build();

            Assert.That(AIPerceptionNetworkProfiles.ServerAuthoritative, Is.SameAs(local));
            Assert.That(local.MaxSnapshotPayloadBytes, Is.EqualTo(NetworkConstants.DefaultMaxPayloadSize));
            Assert.That(local.MaxSnapshotEntries, Is.EqualTo(30));
            Assert.That(handshake.Negotiate(local), Is.EqualTo(AIPerceptionNetworkHandshakeResult.Compatible));
            Assert.That(handshake.Negotiate(changed), Is.EqualTo(AIPerceptionNetworkHandshakeResult.ProfileMismatch));

            var unsupportedRemote = new AIPerceptionManifestHandshakeMessage(
                handshake.ProtocolFingerprint,
                handshake.PerceptionProfileHash,
                AIPerceptionNetworkFeatureFlags.TeamShared,
                AIPerceptionNetworkFeatureFlags.TeamShared,
                1,
                1);
            Assert.That(
                unsupportedRemote.Negotiate(local),
                Is.EqualTo(AIPerceptionNetworkHandshakeResult.RemoteRequirementsUnsupported));

            var missingLocalRequirement = new AIPerceptionManifestHandshakeMessage(
                handshake.ProtocolFingerprint,
                handshake.PerceptionProfileHash,
                AIPerceptionNetworkFeatureFlags.None,
                AIPerceptionNetworkFeatureFlags.None,
                1,
                1);
            Assert.That(
                missingLocalRequirement.Negotiate(local),
                Is.EqualTo(AIPerceptionNetworkHandshakeResult.LocalRequirementsUnsupported));
        }

        [Test]
        public void SyncBridge_WritesCanonicalCallerOwnedSubsetAndReportsLoss()
        {
            var profileBuilder = AIPerceptionNetworkProfiles.CreateServerAuthoritativeBuilder();
            profileBuilder.MaxSnapshotEntries = 1;
            var bridge = new AIPerceptionNetworkSyncBridge(profileBuilder.Build());
            var resolver = new TestTargetResolver();
            var first = new PerceptibleHandle(1, 1);
            var second = new PerceptibleHandle(2, 1);
            var unresolved = new PerceptibleHandle(3, 1);
            resolver.Map(first, new AIPerceptionNetworkTarget(20u, 1));
            resolver.Map(second, new AIPerceptionNetworkTarget(10u, 1));
            DetectionResult[] detections =
            {
                CreateDetection(first, SensorType.Sight),
                CreateDetection(unresolved, SensorType.Sight),
                CreateDetection(second, SensorType.Hearing)
            };
            var entries = new AIPerceptionDetectionEntry[1];

            AIPerceptionDetectionEntryWriteResult result = bridge.WriteDetectionEntries(
                detections,
                resolver,
                entries,
                tick: 20);

            Assert.That(result.Status, Is.EqualTo(AIPerceptionDetectionEntryWriteStatus.Partial));
            Assert.That(result.WrittenCount, Is.EqualTo(1));
            Assert.That(result.UnresolvedCount, Is.EqualTo(1));
            Assert.That(result.CapacityLimitedCount, Is.EqualTo(1));
            Assert.That(entries[0].TargetNetworkId, Is.EqualTo(10u));
            Assert.That(
                bridge.TryCreateSnapshot(
                    100u,
                    AIPerceptionNetworkSensorKind.Any,
                    entries,
                    20,
                    1,
                    3u,
                    out AIPerceptionDetectionSnapshotMessage snapshot),
                Is.EqualTo(AIPerceptionNetworkMessageValidationResult.Valid));
            Assert.That(snapshot.EntryCount, Is.EqualTo(1));
            Assert.That(snapshot.StateHash, Is.EqualTo(AIPerceptionNetworkHash.Compute(entries)));
        }

        [Test]
        public void SyncBridge_RejectsSnapshotWhenProfileDoesNotEnableSnapshots()
        {
            var bridge = new AIPerceptionNetworkSyncBridge(
                AIPerceptionNetworkProfiles.SharedTeamAwareness);

            AIPerceptionNetworkMessageValidationResult result = bridge.TryCreateSnapshot(
                100u,
                AIPerceptionNetworkSensorKind.Any,
                ReadOnlySpan<AIPerceptionDetectionEntry>.Empty,
                20,
                1,
                3u,
                out AIPerceptionDetectionSnapshotMessage snapshot);

            Assert.That(
                result,
                Is.EqualTo(AIPerceptionNetworkMessageValidationResult.UnsupportedFeature));
            Assert.That(snapshot, Is.EqualTo(default(AIPerceptionDetectionSnapshotMessage)));
        }

        [Test]
        public void CodecAndBridge_WarmedPathsAllocateNoManagedMemory()
        {
            var bridge = new AIPerceptionNetworkSyncBridge();
            var resolver = new TestTargetResolver();
            var handle = new PerceptibleHandle(1, 1);
            resolver.Map(handle, new AIPerceptionNetworkTarget(10u, 1));
            DetectionResult[] detections = { CreateDetection(handle, SensorType.Sight) };
            var entries = new AIPerceptionDetectionEntry[1];
            var payload = new byte[AIPerceptionNetworkWireCodec.GetSnapshotPayloadBytes(1)];

            RunMeasuredPath(bridge, resolver, detections, entries, payload);
            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                RunMeasuredPath(bridge, resolver, detections, entries, payload);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void AuthorityResolver_RejectsSpoofReplayStaleAndGenerationDrift()
        {
            var resolver = new ServerAuthoritativeAIPerceptionAuthorityResolver();
            var observer = new NetworkedAIPerceptionObserver(
                100u,
                7,
                700UL,
                2,
                uint.MaxValue,
                false,
                NetworkVector3.Zero,
                5u);
            var local = new AIPerceptionNetworkAuthorityContext(false, true, 7, 5u);
            AIPerceptionDetectionEntry[] entries =
            {
                CreateEntry(10u, AIPerceptionNetworkSensorKind.Sight, 20)
            };
            var snapshot = new AIPerceptionDetectionSnapshotMessage(
                1,
                100u,
                8,
                20,
                AIPerceptionNetworkSensorKind.Any,
                5u,
                1,
                AIPerceptionNetworkHash.Compute(entries));
            var inbound = new AIPerceptionRemoteSnapshotContext(1, 1, true, true, 5u);

            Assert.That(
                resolver.ValidateRemotePerception(in local, in inbound, in observer, in snapshot, entries),
                Is.EqualTo(AIPerceptionRemoteSnapshotResult.Allowed));

            var spoof = new AIPerceptionRemoteSnapshotContext(2, 1, true, true, 5u);
            Assert.That(
                resolver.ValidateRemotePerception(in local, in spoof, in observer, in snapshot, entries),
                Is.EqualTo(AIPerceptionRemoteSnapshotResult.SenderIsNotAuthority));

            var unauthenticated = new AIPerceptionRemoteSnapshotContext(1, 1, false, true, 5u);
            Assert.That(
                resolver.ValidateRemotePerception(
                    in local,
                    in unauthenticated,
                    in observer,
                    in snapshot,
                    entries),
                Is.EqualTo(AIPerceptionRemoteSnapshotResult.UnauthenticatedSender));

            var wrongDirection = new AIPerceptionRemoteSnapshotContext(1, 1, true, false, 5u);
            Assert.That(
                resolver.ValidateRemotePerception(
                    in local,
                    in wrongDirection,
                    in observer,
                    in snapshot,
                    entries),
                Is.EqualTo(AIPerceptionRemoteSnapshotResult.InvalidDirection));

            var replay = new AIPerceptionRemoteSnapshotContext(1, 1, true, true, 5u, true, 8, 20);
            Assert.That(
                resolver.ValidateRemotePerception(in local, in replay, in observer, in snapshot, entries),
                Is.EqualTo(AIPerceptionRemoteSnapshotResult.ReplayedOrOutOfOrderSequence));

            var stale = new AIPerceptionRemoteSnapshotContext(1, 1, true, true, 5u, true, 7, 21);
            Assert.That(
                resolver.ValidateRemotePerception(in local, in stale, in observer, in snapshot, entries),
                Is.EqualTo(AIPerceptionRemoteSnapshotResult.StaleTick));

            var wrongGeneration = new AIPerceptionRemoteSnapshotContext(1, 1, true, true, 4u);
            Assert.That(
                resolver.ValidateRemotePerception(
                    in local,
                    in wrongGeneration,
                    in observer,
                    in snapshot,
                    entries),
                Is.EqualTo(AIPerceptionRemoteSnapshotResult.AuthorityGenerationMismatch));
        }

        [Test]
        public void ObserverResolver_UsesSharedOwnerLayerTeamAndRadiusSemantics()
        {
            var resolver = new AIPerceptionNetworkObserverResolver();
            var observerSource = new TestObserverSource();
            var playerOwner = new TestConnection(2, true) { PlayerId = 700UL };
            var outsideOwnRadius = new TestConnection(3, true);
            var wrongLayerTeam = new TestConnection(4, true);
            var results = new List<INetConnection>(3);
            observerSource.SetObserver(
                2,
                new NetworkInterestObserver(playerOwner, NetworkVector3.Zero, 10f, 0b0001u, 700UL, 0));
            observerSource.SetObserver(
                3,
                new NetworkInterestObserver(
                    outsideOwnRadius,
                    new NetworkVector3(5f, 0f, 0f),
                    4f,
                    0b0001u));
            observerSource.SetObserver(
                4,
                new NetworkInterestObserver(wrongLayerTeam, NetworkVector3.Zero, 10f, 0b0010u, 0UL, 2));
            var observer = new NetworkedAIPerceptionObserver(
                100u,
                99,
                700UL,
                2,
                0b0001u,
                false,
                NetworkVector3.Zero,
                1u);

            var ownerContext = new AIPerceptionReplicationContext(
                in observer,
                NetworkReplicationPolicy.OwnerOnly());
            Assert.That(
                resolver.ResolveObservers(
                    in ownerContext,
                    new INetConnection[] { playerOwner, outsideOwnRadius, wrongLayerTeam },
                    observerSource,
                    results),
                Is.EqualTo(1));
            Assert.That(results[0], Is.SameAs(playerOwner));

            var areaAndTeamContext = new AIPerceptionReplicationContext(
                in observer,
                NetworkReplicationPolicy.TeamOrArea(20f, includeOwner: false));
            Assert.That(
                resolver.ResolveObservers(
                    in areaAndTeamContext,
                    new INetConnection[] { outsideOwnRadius, wrongLayerTeam },
                    observerSource,
                    results),
                Is.Zero);
        }

        private static void RunMeasuredPath(
            AIPerceptionNetworkSyncBridge bridge,
            IAIPerceptionNetworkTargetResolver resolver,
            DetectionResult[] detections,
            AIPerceptionDetectionEntry[] entries,
            byte[] payload)
        {
            AIPerceptionDetectionEntryWriteResult writeResult = bridge.WriteDetectionEntries(
                detections,
                resolver,
                entries,
                10);
            bridge.TryCreateSnapshot(
                1u,
                AIPerceptionNetworkSensorKind.Any,
                entries.AsSpan(0, writeResult.WrittenCount),
                10,
                1,
                1u,
                out AIPerceptionDetectionSnapshotMessage snapshot);
            AIPerceptionNetworkWireCodec.TryWriteDetectionSnapshot(
                in snapshot,
                entries.AsSpan(0, writeResult.WrittenCount),
                payload,
                out _);
        }

        private static AIPerceptionDetectionEntry CreateEntry(
            uint targetId,
            AIPerceptionNetworkSensorKind sensorKind,
            int tick)
        {
            return new AIPerceptionDetectionEntry(
                targetId,
                (int)targetId,
                sensorKind,
                AIPerceptionDetectionFlags.None,
                new NetworkVector3(targetId, 2f, 3f),
                12.5f,
                0.75f,
                tick,
                1);
        }

        private static DetectionResult CreateDetection(PerceptibleHandle handle, SensorType sensorType)
        {
            return new DetectionResult
            {
                Target = handle,
                Distance = 12.5f,
                LastKnownPosition = new float3(1f, 2f, 3f),
                DetectionTime = 1.25f,
                Visibility = 0.75f,
                SensorType = sensorType,
                IsFromMemory = false
            };
        }

        private static void AssertDescriptor(
            NetworkMessageCatalog catalog,
            ushort messageId,
            string contractId,
            NetworkChannel channel,
            int maxPayloadBytes)
        {
            Assert.That(catalog.TryGet(messageId, out NetworkMessageDescriptor descriptor), Is.True);
            Assert.That(descriptor.Owner, Is.EqualTo(AIPerceptionNetworkProtocol.MessageOwner));
            Assert.That(descriptor.ContractId, Is.EqualTo(contractId));
            Assert.That(descriptor.DefaultChannel, Is.EqualTo(channel));
            Assert.That(descriptor.MaxPayloadSize, Is.EqualTo(maxPayloadBytes));
        }

        private static void AssertEntryEqual(
            in AIPerceptionDetectionEntry expected,
            in AIPerceptionDetectionEntry actual)
        {
            Assert.That(actual.TargetNetworkId, Is.EqualTo(expected.TargetNetworkId));
            Assert.That(actual.PerceptibleTypeId, Is.EqualTo(expected.PerceptibleTypeId));
            Assert.That(actual.SensorKind, Is.EqualTo(expected.SensorKind));
            Assert.That(actual.Flags, Is.EqualTo(expected.Flags));
            Assert.That(actual.LastKnownPosition.X, Is.EqualTo(expected.LastKnownPosition.X));
            Assert.That(actual.LastKnownPosition.Y, Is.EqualTo(expected.LastKnownPosition.Y));
            Assert.That(actual.LastKnownPosition.Z, Is.EqualTo(expected.LastKnownPosition.Z));
            Assert.That(actual.Distance, Is.EqualTo(expected.Distance));
            Assert.That(actual.Visibility, Is.EqualTo(expected.Visibility));
            Assert.That(actual.DetectionTick, Is.EqualTo(expected.DetectionTick));
            Assert.That(actual.SourceSensorId, Is.EqualTo(expected.SourceSensorId));
        }

        private sealed class TestTargetResolver : IAIPerceptionNetworkTargetResolver
        {
            private readonly Dictionary<PerceptibleHandle, AIPerceptionNetworkTarget> _targets =
                new Dictionary<PerceptibleHandle, AIPerceptionNetworkTarget>();

            public void Map(PerceptibleHandle handle, AIPerceptionNetworkTarget target)
            {
                _targets[handle] = target;
            }

            public bool TryResolveNetworkTarget(PerceptibleHandle handle, out AIPerceptionNetworkTarget target)
            {
                return _targets.TryGetValue(handle, out target);
            }
        }

        private sealed class TestObserverSource : IAIPerceptionNetworkObserverSource
        {
            private readonly Dictionary<int, NetworkInterestObserver> _observers =
                new Dictionary<int, NetworkInterestObserver>();

            public void SetObserver(int connectionId, in NetworkInterestObserver observer)
            {
                _observers[connectionId] = observer;
            }

            public bool TryGetObserver(int connectionId, out NetworkInterestObserver observer)
            {
                return _observers.TryGetValue(connectionId, out observer);
            }
        }

        private sealed class TestConnection : INetConnection
        {
            public TestConnection(int connectionId, bool isAuthenticated)
            {
                ConnectionId = connectionId;
                IsAuthenticated = isAuthenticated;
            }

            public int ConnectionId { get; }
            public string RemoteAddress => string.Empty;
            public bool IsConnected => true;
            public bool IsAuthenticated { get; }
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
