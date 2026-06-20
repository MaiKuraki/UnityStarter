using System;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.Networking;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Networking
{
    public readonly struct AIPerceptionNetworkTarget
    {
        public readonly uint NetworkId;
        public readonly int PerceptibleTypeId;

        public AIPerceptionNetworkTarget(uint networkId, int perceptibleTypeId)
        {
            NetworkId = networkId;
            PerceptibleTypeId = perceptibleTypeId;
        }

        public bool IsValid => NetworkId != 0u;
    }

    public interface IAIPerceptionNetworkTargetResolver
    {
        bool TryResolveNetworkTarget(PerceptibleHandle handle, out AIPerceptionNetworkTarget target);
    }

    public sealed class AIPerceptionNetworkSyncBridge
    {
        private const int ESTIMATED_DETECTION_ENTRY_BYTES = 48;

        private readonly AIPerceptionNetworkProfile _profile;

        public AIPerceptionNetworkSyncBridge(AIPerceptionNetworkProfile profile = null)
        {
            _profile = profile ?? AIPerceptionNetworkProfiles.ServerAuthoritative;
        }

        public AIPerceptionNetworkProfile Profile => _profile;

        public bool TryCreateDetectionEvent(
            uint observerNetworkId,
            in DetectionResult detection,
            IAIPerceptionNetworkTargetResolver targetResolver,
            int tick,
            ushort sequence,
            AIPerceptionNetworkEventKind eventKind,
            out AIPerceptionDetectionEventMessage message,
            int sourceSensorId = 0,
            AIPerceptionDetectionFlags extraFlags = AIPerceptionDetectionFlags.None)
        {
            message = default;
            if (observerNetworkId == 0u || targetResolver == null || eventKind == AIPerceptionNetworkEventKind.Unknown)
            {
                return false;
            }

            if (!TryCreateEntry(detection, targetResolver, tick, sourceSensorId, extraFlags, out AIPerceptionDetectionEntry entry))
            {
                return false;
            }

            uint stateHash = AIPerceptionNetworkHash.Compute(entry);
            message = new AIPerceptionDetectionEventMessage(
                observerNetworkId,
                sequence,
                tick,
                eventKind,
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                stateHash,
                entry);
            return true;
        }

        public int WriteDetectionEntries(
            ReadOnlySpan<DetectionResult> detections,
            IAIPerceptionNetworkTargetResolver targetResolver,
            Span<AIPerceptionDetectionEntry> entries,
            int tick,
            int sourceSensorId = 0,
            AIPerceptionDetectionFlags extraFlags = AIPerceptionDetectionFlags.None)
        {
            if (targetResolver == null || entries.Length == 0)
            {
                return 0;
            }

            int written = 0;
            int max = Math.Min(detections.Length, entries.Length);
            max = Math.Min(max, _profile.MaxSnapshotEntries);

            for (int i = 0; i < detections.Length && written < max; i++)
            {
                if (TryCreateEntry(detections[i], targetResolver, tick, sourceSensorId, extraFlags, out AIPerceptionDetectionEntry entry))
                {
                    entries[written++] = entry;
                }
            }

            return written;
        }

        public AIPerceptionDetectionSnapshotMessage CreateSnapshot(
            uint observerNetworkId,
            AIPerceptionNetworkSensorKind sensorKind,
            ReadOnlySpan<AIPerceptionDetectionEntry> entries,
            int tick,
            ushort sequence)
        {
            if (observerNetworkId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(observerNetworkId));
            }

            ValidateSnapshotEntries(entries.Length);

            var payload = new AIPerceptionDetectionEntry[entries.Length];
            entries.CopyTo(payload);

            uint stateHash = AIPerceptionNetworkHash.Compute(entries);
            return new AIPerceptionDetectionSnapshotMessage(
                observerNetworkId,
                sequence,
                tick,
                sensorKind,
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                stateHash,
                payload);
        }

        public AIPerceptionFullStateRequestMessage CreateFullStateRequest(
            uint observerNetworkId,
            AIPerceptionNetworkSensorKind sensorKind,
            int tick,
            ushort sequence,
            uint lastKnownStateHash = 0u)
        {
            if (observerNetworkId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(observerNetworkId));
            }

            return new AIPerceptionFullStateRequestMessage(
                observerNetworkId,
                sequence,
                tick,
                sensorKind,
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                lastKnownStateHash);
        }

        public AIPerceptionAuthorityTransferMessage CreateAuthorityTransfer(
            uint observerNetworkId,
            int previousOwnerConnectionId,
            int newOwnerConnectionId,
            ulong previousOwnerPlayerId,
            ulong newOwnerPlayerId,
            uint authorityGeneration,
            ushort snapshotSequence,
            int snapshotTick,
            uint snapshotStateHash)
        {
            if (observerNetworkId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(observerNetworkId));
            }

            return new AIPerceptionAuthorityTransferMessage(
                observerNetworkId,
                previousOwnerConnectionId,
                newOwnerConnectionId,
                previousOwnerPlayerId,
                newOwnerPlayerId,
                authorityGeneration,
                snapshotSequence,
                snapshotTick,
                snapshotStateHash);
        }

        public static AIPerceptionNetworkSensorKind ToNetworkSensorKind(SensorType sensorType)
        {
            return sensorType switch
            {
                SensorType.Sight => AIPerceptionNetworkSensorKind.Sight,
                SensorType.Hearing => AIPerceptionNetworkSensorKind.Hearing,
                SensorType.Proximity => AIPerceptionNetworkSensorKind.Proximity,
                SensorType.Custom => AIPerceptionNetworkSensorKind.Custom,
                _ => AIPerceptionNetworkSensorKind.Any
            };
        }

        public static NetworkVector3 ToNetworkVector3(float3 value)
        {
            return new NetworkVector3(value.x, value.y, value.z);
        }

        private static AIPerceptionDetectionFlags CreateFlags(
            in DetectionResult detection,
            AIPerceptionDetectionFlags extraFlags)
        {
            AIPerceptionDetectionFlags flags = extraFlags;
            if (detection.IsFromMemory)
            {
                flags |= AIPerceptionDetectionFlags.FromMemory;
            }

            return flags;
        }

        private static bool TryCreateEntry(
            in DetectionResult detection,
            IAIPerceptionNetworkTargetResolver targetResolver,
            int tick,
            int sourceSensorId,
            AIPerceptionDetectionFlags extraFlags,
            out AIPerceptionDetectionEntry entry)
        {
            entry = default;
            if (targetResolver == null || !targetResolver.TryResolveNetworkTarget(detection.Target, out AIPerceptionNetworkTarget target) || !target.IsValid)
            {
                return false;
            }

            NetworkVector3 position = ToNetworkVector3(detection.LastKnownPosition);
            if (!position.IsFinite())
            {
                return false;
            }

            entry = new AIPerceptionDetectionEntry(
                target.NetworkId,
                target.PerceptibleTypeId,
                ToNetworkSensorKind((SensorType)detection.SensorType),
                CreateFlags(detection, extraFlags),
                position,
                detection.Distance,
                detection.Visibility,
                tick,
                sourceSensorId);
            return entry.IsValid;
        }

        private void ValidateSnapshotEntries(int entryCount)
        {
            if (entryCount > _profile.MaxSnapshotEntries)
            {
                throw new InvalidOperationException($"AIPerception snapshot entry count {entryCount} exceeds max entry count {_profile.MaxSnapshotEntries}.");
            }

            int estimatedBytes = entryCount * ESTIMATED_DETECTION_ENTRY_BYTES;
            if (estimatedBytes > _profile.MaxSnapshotPayloadBytes)
            {
                throw new InvalidOperationException($"AIPerception snapshot estimated payload size {estimatedBytes} exceeds max payload size {_profile.MaxSnapshotPayloadBytes}.");
            }
        }
    }
}

