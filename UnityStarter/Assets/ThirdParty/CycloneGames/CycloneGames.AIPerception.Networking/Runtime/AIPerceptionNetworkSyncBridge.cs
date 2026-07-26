using System;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.Networking;
using System.Threading;
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

    public enum AIPerceptionDetectionEntryWriteStatus : byte
    {
        Invalid = 0,
        Success = 1,
        Partial = 2,
        InvalidArguments = 3
    }

    public readonly struct AIPerceptionDetectionEntryWriteResult
    {
        public readonly AIPerceptionDetectionEntryWriteStatus Status;
        public readonly int WrittenCount;
        public readonly int UnresolvedCount;
        public readonly int InvalidCount;
        public readonly int CapacityLimitedCount;
        public readonly int DuplicateCount;

        public AIPerceptionDetectionEntryWriteResult(
            AIPerceptionDetectionEntryWriteStatus status,
            int writtenCount,
            int unresolvedCount,
            int invalidCount,
            int capacityLimitedCount,
            int duplicateCount)
        {
            Status = status;
            WrittenCount = writtenCount;
            UnresolvedCount = unresolvedCount;
            InvalidCount = invalidCount;
            CapacityLimitedCount = capacityLimitedCount;
            DuplicateCount = duplicateCount;
        }

        public int SkippedCount => UnresolvedCount + InvalidCount + CapacityLimitedCount + DuplicateCount;
        public bool IsComplete => Status == AIPerceptionDetectionEntryWriteStatus.Success;
    }

    public sealed class AIPerceptionNetworkSyncBridge
    {
        public const int MaximumDetectionInputsPerWrite = 4_096;

        private readonly AIPerceptionNetworkProfile _profile;
        private long _writeOperationCount;
        private long _suppliedDetectionCount;
        private int _peakSuppliedDetectionCount;
        private long _scannedDetectionCount;
        private int _peakScannedDetectionCount;
        private long _writtenEntryCount;
        private long _unresolvedCount;
        private long _invalidCount;
        private long _capacityLimitedCount;
        private long _duplicateCount;
        private long _snapshotOperationCount;
        private long _acceptedSnapshotCount;
        private long _rejectedSnapshotCount;
        private int _lastSnapshotEntryCount;
        private int _peakSnapshotEntryCount;
        private int _lastSnapshotPayloadBytes;
        private int _peakSnapshotPayloadBytes;

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
            uint authorityGeneration,
            AIPerceptionNetworkEventKind eventKind,
            out AIPerceptionDetectionEventMessage message,
            int sourceSensorId = 0,
            AIPerceptionDetectionFlags extraFlags = AIPerceptionDetectionFlags.None)
        {
            message = default;
            if (!_profile.HasFeature(AIPerceptionNetworkFeatureFlags.DetectionEvents) ||
                observerNetworkId == 0u || targetResolver == null ||
                !AIPerceptionNetworkMessageValidator.IsKnownEventKind(eventKind))
            {
                return false;
            }

            if (TryCreateEntry(
                    in detection,
                    targetResolver,
                    tick,
                    sourceSensorId,
                    extraFlags,
                    out AIPerceptionDetectionEntry entry) != EntryCreationResult.Success)
            {
                return false;
            }

            message = new AIPerceptionDetectionEventMessage(
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                observerNetworkId,
                sequence,
                tick,
                eventKind,
                authorityGeneration,
                AIPerceptionNetworkHash.Compute(in entry),
                in entry);
            if (AIPerceptionNetworkMessageValidator.Validate(in message) ==
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return true;
            }

            message = default;
            return false;
        }

        /// <summary>
        /// Selects the canonical smallest entries into caller-owned storage without allocation.
        /// The result reports every unresolved, invalid, duplicate, or capacity-limited input.
        /// </summary>
        public AIPerceptionDetectionEntryWriteResult WriteDetectionEntries(
            ReadOnlySpan<DetectionResult> detections,
            IAIPerceptionNetworkTargetResolver targetResolver,
            Span<AIPerceptionDetectionEntry> entries,
            int tick,
            int sourceSensorId = 0,
            AIPerceptionDetectionFlags extraFlags = AIPerceptionDetectionFlags.None)
        {
            if (targetResolver == null || tick < 0 || sourceSensorId < 0 ||
                (extraFlags & ~AIPerceptionNetworkMessageValidator.KnownDetectionFlags) != 0)
            {
                var invalidResult = new AIPerceptionDetectionEntryWriteResult(
                    AIPerceptionDetectionEntryWriteStatus.InvalidArguments,
                    0,
                    0,
                    detections.Length,
                    0,
                    0);
                RecordWriteResult(detections.Length, 0, in invalidResult);
                return invalidResult;
            }

            if (detections.Length > MaximumDetectionInputsPerWrite)
            {
                var boundedResult = new AIPerceptionDetectionEntryWriteResult(
                    AIPerceptionDetectionEntryWriteStatus.Partial,
                    0,
                    0,
                    0,
                    detections.Length,
                    0);
                RecordWriteResult(detections.Length, 0, in boundedResult);
                return boundedResult;
            }

            int capacity = Math.Min(entries.Length, _profile.MaxSnapshotEntries);
            capacity = Math.Min(
                capacity,
                AIPerceptionNetworkWireCodec.GetMaxSnapshotEntries(_profile.MaxSnapshotPayloadBytes));

            int written = 0;
            int unresolved = 0;
            int invalid = 0;
            int capacityLimited = 0;
            int duplicates = 0;

            for (int i = 0; i < detections.Length; i++)
            {
                EntryCreationResult creationResult = TryCreateEntry(
                    in detections[i],
                    targetResolver,
                    tick,
                    sourceSensorId,
                    extraFlags,
                    out AIPerceptionDetectionEntry entry);
                if (creationResult == EntryCreationResult.Unresolved)
                {
                    unresolved++;
                    continue;
                }

                if (creationResult != EntryCreationResult.Success)
                {
                    invalid++;
                    continue;
                }

                EntryInsertionResult insertionResult = InsertCanonical(entries, capacity, ref written, in entry);
                if (insertionResult == EntryInsertionResult.Duplicate)
                {
                    duplicates++;
                }
                else if (insertionResult == EntryInsertionResult.CapacityLimited)
                {
                    capacityLimited++;
                }
            }

            bool complete = unresolved == 0 && invalid == 0 && capacityLimited == 0 && duplicates == 0;
            var result = new AIPerceptionDetectionEntryWriteResult(
                complete
                    ? AIPerceptionDetectionEntryWriteStatus.Success
                    : AIPerceptionDetectionEntryWriteStatus.Partial,
                written,
                unresolved,
                invalid,
                capacityLimited,
                duplicates);
            RecordWriteResult(detections.Length, detections.Length, in result);
            return result;
        }

        public AIPerceptionNetworkMessageValidationResult TryCreateSnapshot(
            uint observerNetworkId,
            AIPerceptionNetworkSensorKind sensorKind,
            ReadOnlySpan<AIPerceptionDetectionEntry> entries,
            int tick,
            ushort sequence,
            uint authorityGeneration,
            out AIPerceptionDetectionSnapshotMessage message)
        {
            message = default;
            if (!_profile.HasFeature(AIPerceptionNetworkFeatureFlags.DetectionSnapshots))
            {
                return RecordSnapshotResult(
                    AIPerceptionNetworkMessageValidationResult.UnsupportedFeature,
                    entries.Length);
            }

            if (entries.Length > _profile.MaxSnapshotEntries ||
                AIPerceptionNetworkWireCodec.GetSnapshotPayloadBytes(entries.Length) >
                _profile.MaxSnapshotPayloadBytes)
            {
                return RecordSnapshotResult(
                    AIPerceptionNetworkMessageValidationResult.EntryCountOutOfRange,
                    entries.Length);
            }

            message = new AIPerceptionDetectionSnapshotMessage(
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                observerNetworkId,
                sequence,
                tick,
                sensorKind,
                authorityGeneration,
                checked((ushort)entries.Length),
                AIPerceptionNetworkHash.Compute(entries));
            AIPerceptionNetworkMessageValidationResult result =
                AIPerceptionNetworkMessageValidator.Validate(in message, entries);
            if (result != AIPerceptionNetworkMessageValidationResult.Valid)
            {
                message = default;
            }

            return RecordSnapshotResult(result, entries.Length);
        }

        /// <summary>Returns lock-free counters without retaining any message or entry data.</summary>
        public AIPerceptionNetworkMemoryStats GetMemoryStats()
        {
            return new AIPerceptionNetworkMemoryStats(
                _profile.MaxSnapshotEntries,
                _profile.MaxSnapshotPayloadBytes,
                MaximumDetectionInputsPerWrite,
                Interlocked.Read(ref _writeOperationCount),
                Interlocked.Read(ref _suppliedDetectionCount),
                Volatile.Read(ref _peakSuppliedDetectionCount),
                Interlocked.Read(ref _scannedDetectionCount),
                Volatile.Read(ref _peakScannedDetectionCount),
                Interlocked.Read(ref _writtenEntryCount),
                Interlocked.Read(ref _unresolvedCount),
                Interlocked.Read(ref _invalidCount),
                Interlocked.Read(ref _capacityLimitedCount),
                Interlocked.Read(ref _duplicateCount),
                Interlocked.Read(ref _snapshotOperationCount),
                Interlocked.Read(ref _acceptedSnapshotCount),
                Interlocked.Read(ref _rejectedSnapshotCount),
                Volatile.Read(ref _lastSnapshotEntryCount),
                Volatile.Read(ref _peakSnapshotEntryCount),
                Volatile.Read(ref _lastSnapshotPayloadBytes),
                Volatile.Read(ref _peakSnapshotPayloadBytes));
        }

        public AIPerceptionFullStateRequestMessage CreateFullStateRequest(
            uint observerNetworkId,
            AIPerceptionNetworkSensorKind sensorKind,
            int tick,
            ushort sequence,
            uint expectedAuthorityGeneration,
            ulong lastKnownStateHash = 0UL)
        {
            var message = new AIPerceptionFullStateRequestMessage(
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                observerNetworkId,
                sequence,
                tick,
                sensorKind,
                expectedAuthorityGeneration,
                lastKnownStateHash);
            if (!message.IsValid)
            {
                throw new ArgumentException("Full-state request fields do not form a valid v1 message.");
            }

            return message;
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
            ulong snapshotStateHash)
        {
            var message = new AIPerceptionAuthorityTransferMessage(
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                observerNetworkId,
                previousOwnerConnectionId,
                newOwnerConnectionId,
                previousOwnerPlayerId,
                newOwnerPlayerId,
                authorityGeneration,
                snapshotSequence,
                snapshotTick,
                snapshotStateHash);
            if (!message.IsValid)
            {
                throw new ArgumentException("Authority-transfer fields do not form a valid v1 message.");
            }

            return message;
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
            return new NetworkVector3(
                CanonicalizeZero(value.x),
                CanonicalizeZero(value.y),
                CanonicalizeZero(value.z));
        }

        private void RecordWriteResult(
            int suppliedCount,
            int scannedCount,
            in AIPerceptionDetectionEntryWriteResult result)
        {
            SaturatingIncrement(ref _writeOperationCount);
            SaturatingAdd(ref _suppliedDetectionCount, suppliedCount);
            UpdatePeak(ref _peakSuppliedDetectionCount, suppliedCount);
            SaturatingAdd(ref _scannedDetectionCount, scannedCount);
            UpdatePeak(ref _peakScannedDetectionCount, scannedCount);
            SaturatingAdd(ref _writtenEntryCount, result.WrittenCount);
            SaturatingAdd(ref _unresolvedCount, result.UnresolvedCount);
            SaturatingAdd(ref _invalidCount, result.InvalidCount);
            SaturatingAdd(ref _capacityLimitedCount, result.CapacityLimitedCount);
            SaturatingAdd(ref _duplicateCount, result.DuplicateCount);
        }

        private AIPerceptionNetworkMessageValidationResult RecordSnapshotResult(
            AIPerceptionNetworkMessageValidationResult result,
            int entryCount)
        {
            int payloadBytes = entryCount <= AIPerceptionNetworkProtocol.MAX_SNAPSHOT_ENTRIES
                ? Math.Max(0, AIPerceptionNetworkWireCodec.GetSnapshotPayloadBytes(entryCount))
                : 0;
            SaturatingIncrement(ref _snapshotOperationCount);
            if (result == AIPerceptionNetworkMessageValidationResult.Valid)
            {
                SaturatingIncrement(ref _acceptedSnapshotCount);
            }
            else
            {
                SaturatingIncrement(ref _rejectedSnapshotCount);
            }

            Volatile.Write(ref _lastSnapshotEntryCount, entryCount);
            Volatile.Write(ref _lastSnapshotPayloadBytes, payloadBytes);
            UpdatePeak(ref _peakSnapshotEntryCount, entryCount);
            UpdatePeak(ref _peakSnapshotPayloadBytes, payloadBytes);
            return result;
        }

        private static void SaturatingIncrement(ref long target)
        {
            SaturatingAdd(ref target, 1L);
        }

        private static void SaturatingAdd(ref long target, long value)
        {
            if (value <= 0L)
            {
                return;
            }

            long current = Interlocked.Read(ref target);
            while (current != long.MaxValue)
            {
                long next = current > long.MaxValue - value ? long.MaxValue : current + value;
                long observed = Interlocked.CompareExchange(ref target, next, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }

        private static void UpdatePeak(ref int target, int candidate)
        {
            int current = Volatile.Read(ref target);
            while (candidate > current)
            {
                int observed = Interlocked.CompareExchange(ref target, candidate, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }

        private static EntryInsertionResult InsertCanonical(
            Span<AIPerceptionDetectionEntry> entries,
            int capacity,
            ref int count,
            in AIPerceptionDetectionEntry entry)
        {
            int low = 0;
            int high = count;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = AIPerceptionNetworkHash.CompareCanonical(in entries[middle], in entry);
                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            if (low < count && AIPerceptionNetworkHash.CompareCanonical(in entries[low], in entry) == 0)
            {
                return EntryInsertionResult.Duplicate;
            }

            if (capacity == 0 || (count == capacity && low == capacity))
            {
                return EntryInsertionResult.CapacityLimited;
            }

            int last = count < capacity ? count : capacity - 1;
            for (int i = last; i > low; i--)
            {
                entries[i] = entries[i - 1];
            }

            entries[low] = entry;
            if (count < capacity)
            {
                count++;
                return EntryInsertionResult.Inserted;
            }

            return EntryInsertionResult.CapacityLimited;
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

        private static EntryCreationResult TryCreateEntry(
            in DetectionResult detection,
            IAIPerceptionNetworkTargetResolver targetResolver,
            int tick,
            int sourceSensorId,
            AIPerceptionDetectionFlags extraFlags,
            out AIPerceptionDetectionEntry entry)
        {
            entry = default;
            if (!targetResolver.TryResolveNetworkTarget(
                    detection.Target,
                    out AIPerceptionNetworkTarget target) || !target.IsValid)
            {
                return EntryCreationResult.Unresolved;
            }

            AIPerceptionNetworkSensorKind sensorKind = ToNetworkSensorKind(detection.SensorType);
            if (!AIPerceptionNetworkMessageValidator.IsConcreteSensorKind(sensorKind))
            {
                return EntryCreationResult.Invalid;
            }

            entry = new AIPerceptionDetectionEntry(
                target.NetworkId,
                target.PerceptibleTypeId,
                sensorKind,
                CreateFlags(in detection, extraFlags),
                ToNetworkVector3(detection.LastKnownPosition),
                CanonicalizeZero(detection.Distance),
                CanonicalizeZero(detection.Visibility),
                tick,
                sourceSensorId);
            return AIPerceptionNetworkMessageValidator.Validate(in entry) ==
                   AIPerceptionNetworkMessageValidationResult.Valid
                ? EntryCreationResult.Success
                : EntryCreationResult.Invalid;
        }

        private static float CanonicalizeZero(float value)
        {
            return value == 0f ? 0f : value;
        }

        private enum EntryCreationResult : byte
        {
            Invalid = 0,
            Success = 1,
            Unresolved = 2
        }

        private enum EntryInsertionResult : byte
        {
            Inserted = 0,
            Duplicate = 1,
            CapacityLimited = 2
        }
    }
}
