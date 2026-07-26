using System;
using System.Collections.Generic;
using System.IO;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Networking;

namespace CycloneGames.BehaviorTree.Networking
{
    /// <summary>
    /// Caller-owned receive cursor for one networked behavior-tree stream.
    /// Reset the cursor explicitly when authority changes or a new stream begins.
    /// </summary>
    public struct BehaviorTreePayloadReceiveState
    {
        private bool _hasAcceptedPayload;
        private ushort _lastSequence;
        private int _lastTick;
        private uint _authorityGeneration;

        public BehaviorTreePayloadReceiveState(
            uint targetNetworkId,
            ulong treeTemplateHash,
            uint authorityGeneration = 0u)
        {
            if (targetNetworkId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(targetNetworkId));
            }

            TargetNetworkId = targetNetworkId;
            TreeTemplateHash = treeTemplateHash;
            _authorityGeneration = authorityGeneration;
            _hasAcceptedPayload = false;
            _lastSequence = 0;
            _lastTick = 0;
        }

        public uint TargetNetworkId { get; }
        public ulong TreeTemplateHash { get; }
        public uint AuthorityGeneration => _authorityGeneration;
        public bool HasAcceptedPayload => _hasAcceptedPayload;
        public ushort LastSequence => _lastSequence;
        public int LastTick => _lastTick;

        public void ResetProgress()
        {
            _hasAcceptedPayload = false;
            _lastSequence = 0;
            _lastTick = 0;
        }

        public void ResetProgress(uint authorityGeneration)
        {
            _authorityGeneration = authorityGeneration;
            ResetProgress();
        }

        internal bool CanAccept(in BehaviorTreeStatePayloadMessage message)
        {
            if (TargetNetworkId == 0u ||
                message.TargetNetworkId != TargetNetworkId ||
                message.TreeTemplateHash != TreeTemplateHash ||
                message.AuthorityGeneration != _authorityGeneration ||
                message.Tick < 0)
            {
                return false;
            }

            if (!_hasAcceptedPayload)
            {
                return true;
            }

            return message.Tick >= _lastTick && IsNewerSequence(message.Sequence, _lastSequence);
        }

        internal void RecordAccepted(in BehaviorTreeStatePayloadMessage message)
        {
            _hasAcceptedPayload = true;
            _lastSequence = message.Sequence;
            _lastTick = message.Tick;
        }

        private static bool IsNewerSequence(ushort candidate, ushort baseline)
        {
            ushort distance = unchecked((ushort)(candidate - baseline));
            return distance != 0 && distance < 0x8000;
        }
    }

    public sealed class BehaviorTreeNetworkSyncBridge : IDisposable
    {
        private readonly BehaviorTreeNetworkProfile _profile;
        private readonly BTStateSnapshotBuffer _snapshotBuffer = new BTStateSnapshotBuffer();
        private readonly int _ownerThreadId;
        private long _capturedSnapshotCount;
        private long _capturedDeltaCount;
        private long _capturedHashOnlyCount;
        private long _suppressedDeltaCount;
        private long _outgoingPayloadBytes;
        private int _peakOutgoingPayloadBytes;
        private long _receivedPayloadCount;
        private long _acceptedPayloadCount;
        private long _rejectedPayloadCount;
        private long _incomingPayloadBytes;
        private int _peakIncomingPayloadBytes;
        private long _acceptedSnapshotCount;
        private long _acceptedDeltaCount;
        private long _acceptedHashOnlyCount;
        private bool _disposed;

        public BehaviorTreeNetworkSyncBridge(BehaviorTreeNetworkProfile profile = null)
        {
            _profile = profile ?? BehaviorTreeNetworkProfiles.ServerAuthoritative;
            _ownerThreadId = Environment.CurrentManagedThreadId;
        }

        public BehaviorTreeNetworkProfile Profile => _profile;
        public int EffectiveMaxSnapshotPayloadBytes => GetEffectivePayloadBudget(_profile.MaxSnapshotPayloadBytes);
        public int EffectiveMaxDeltaPayloadBytes => GetEffectivePayloadBudget(_profile.MaxDeltaPayloadBytes);

        public BehaviorTreeNetworkMemoryStats GetMemoryStats()
        {
            EnsureUsable();
            BTStateSnapshotBufferMemoryStats bufferStats = _snapshotBuffer.GetMemoryStats();
            return new BehaviorTreeNetworkMemoryStats(
                EffectiveMaxSnapshotPayloadBytes,
                EffectiveMaxDeltaPayloadBytes,
                _profile.MaxTrackedBlackboardKeys,
                retainedPayloadBytes: 0,
                bufferStats.NodeStateCapacity,
                bufferStats.NodeAuxiliaryCapacity,
                bufferStats.TraversalNodeCapacity,
                bufferStats.TraversalStackCapacity,
                bufferStats.BlackboardStreamCapacityBytes,
                bufferStats.SnapshotStreamCapacityBytes,
                _capturedSnapshotCount,
                _capturedDeltaCount,
                _capturedHashOnlyCount,
                _suppressedDeltaCount,
                _outgoingPayloadBytes,
                _peakOutgoingPayloadBytes,
                _receivedPayloadCount,
                _acceptedPayloadCount,
                _rejectedPayloadCount,
                _incomingPayloadBytes,
                _peakIncomingPayloadBytes,
                _acceptedSnapshotCount,
                _acceptedDeltaCount,
                _acceptedHashOnlyCount,
                retainedHistoryEntryCount: 0,
                maximumRetainedHistoryEntryCount: 0);
        }

        public BehaviorTreeStatePayloadMessage CaptureSnapshot(
            uint targetNetworkId,
            RuntimeBehaviorTree tree,
            int tick,
            ushort sequence,
            ulong treeTemplateHash = 0UL,
            uint authorityGeneration = 0u)
        {
            EnsureUsable();
            if (targetNetworkId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(targetNetworkId));
            }

            if (tree == null)
            {
                throw new ArgumentNullException(nameof(tree));
            }

            BTNetworkSync.Limits limits = CreateSnapshotLimits(EffectiveMaxSnapshotPayloadBytes);
            BTStateSnapshot snapshot = BTNetworkSync.CaptureSnapshot(tree, _snapshotBuffer, limits);
            ArraySegment<byte> segment = BTNetworkSync.SerializeSnapshot(snapshot, _snapshotBuffer, limits);

            var payload = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array, segment.Offset, payload, 0, segment.Count);
            RecordOutgoingPayload(BehaviorTreeNetworkPayloadKind.FullSnapshot, payload.Length);

            return new BehaviorTreeStatePayloadMessage(
                targetNetworkId,
                sequence,
                tick,
                BehaviorTreeNetworkPayloadKind.FullSnapshot,
                treeTemplateHash,
                snapshot.BlackboardHash,
                snapshot.TreeStateHash,
                payload,
                authorityGeneration);
        }

        public bool TryCreateBlackboardDelta(
            uint targetNetworkId,
            RuntimeBehaviorTree tree,
            BTBlackboardDelta deltaTracker,
            int tick,
            ushort sequence,
            ulong treeTemplateHash,
            out BehaviorTreeStatePayloadMessage message,
            uint authorityGeneration = 0u)
        {
            EnsureUsable();
            message = default;
            if (targetNetworkId == 0u || tree?.Blackboard == null || deltaTracker == null)
            {
                _suppressedDeltaCount++;
                return false;
            }

            if (!deltaTracker.TryFlush(
                    tree.Blackboard,
                    EffectiveMaxDeltaPayloadBytes,
                    out ArraySegment<byte> segment))
            {
                _suppressedDeltaCount++;
                return false;
            }

            var payload = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array, segment.Offset, payload, 0, segment.Count);
            RecordOutgoingPayload(BehaviorTreeNetworkPayloadKind.BlackboardDelta, payload.Length);

            ulong blackboardHash = tree.Blackboard.ComputeHash(RuntimeBlackboardNetworkScope.Networked);
            ulong treeStateHash = ComputeLiveTreeStateHash(tree, blackboardHash);
            message = new BehaviorTreeStatePayloadMessage(
                targetNetworkId,
                sequence,
                tick,
                BehaviorTreeNetworkPayloadKind.BlackboardDelta,
                treeTemplateHash,
                blackboardHash,
                treeStateHash,
                payload,
                authorityGeneration);
            return true;
        }

        public BehaviorTreeStatePayloadMessage CreateHashOnlyMessage(
            uint targetNetworkId,
            RuntimeBehaviorTree tree,
            int tick,
            ushort sequence,
            ulong treeTemplateHash = 0UL,
            uint authorityGeneration = 0u)
        {
            EnsureUsable();
            if (targetNetworkId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(targetNetworkId));
            }

            if (tree == null)
            {
                throw new ArgumentNullException(nameof(tree));
            }

            ulong blackboardHash = tree.Blackboard != null
                ? tree.Blackboard.ComputeHash(RuntimeBlackboardNetworkScope.Networked)
                : 0UL;
            ulong treeStateHash = ComputeLiveTreeStateHash(tree, blackboardHash);
            RecordOutgoingPayload(BehaviorTreeNetworkPayloadKind.HashOnly, payloadBytes: 0);
            return new BehaviorTreeStatePayloadMessage(
                targetNetworkId,
                sequence,
                tick,
                BehaviorTreeNetworkPayloadKind.HashOnly,
                treeTemplateHash,
                blackboardHash,
                treeStateHash,
                null,
                authorityGeneration);
        }

        public bool TryApplyPayload(
            RuntimeBehaviorTree tree,
            in BehaviorTreeStatePayloadMessage message,
            ref BehaviorTreePayloadReceiveState receiveState)
        {
            EnsureUsable();
            RecordIncomingPayload(message.Payload != null ? message.Payload.Length : 0);
            if (tree == null || tree.Blackboard == null || !message.IsValid || !receiveState.CanAccept(message))
            {
                return RejectIncomingPayload();
            }

            if (!ValidateIncomingPayload(message))
            {
                return RejectIncomingPayload();
            }

            if (message.PayloadKind == BehaviorTreeNetworkPayloadKind.FullSnapshot)
            {
                BTNetworkSync.Limits limits;
                BTStateSnapshot snapshot;
                try
                {
                    limits = CreateSnapshotLimits(EffectiveMaxSnapshotPayloadBytes);
                    snapshot = BTNetworkSync.DeserializeSnapshot(message.Payload, limits);
                    if (!snapshot.IsValid ||
                        snapshot.BlackboardHash != message.BlackboardHash ||
                        snapshot.TreeStateHash != message.TreeStateHash ||
                        BTNetworkSync.ComputeTreeStateHash(snapshot) != message.TreeStateHash ||
                        !BTNetworkSync.DoesExecutionStateMatch(tree, snapshot, _snapshotBuffer, limits) ||
                        ComputeSnapshotBlackboardHash(snapshot, tree.Blackboard, limits) != message.BlackboardHash)
                    {
                        return RejectIncomingPayload();
                    }

                }
                catch (InvalidDataException)
                {
                    return RejectIncomingPayload();
                }
                catch (IOException)
                {
                    return RejectIncomingPayload();
                }
                catch (KeyNotFoundException)
                {
                    return RejectIncomingPayload();
                }
                catch (InvalidOperationException)
                {
                    return RejectIncomingPayload();
                }
                catch (ArgumentException)
                {
                    return RejectIncomingPayload();
                }
                catch (OverflowException)
                {
                    return RejectIncomingPayload();
                }

                // The payload is now fully parsed and validated. Commit outside the
                // untrusted-input catch block so observer and owner-state failures are
                // visible to the caller after the blackboard has committed.
                BTNetworkSync.ApplyBlackboardSnapshot(tree, snapshot, limits);
                if (tree.Blackboard.ComputeHash(RuntimeBlackboardNetworkScope.Snapshot) != message.BlackboardHash)
                {
                    throw new InvalidOperationException(
                        "A full behavior-tree snapshot committed, but application-side mutation changed its blackboard hash.");
                }

                if (_profile.WakeTreeOnRemoteDelta)
                {
                    tree.WakeUp();
                }

                receiveState.RecordAccepted(message);
                return AcceptIncomingPayload(BehaviorTreeNetworkPayloadKind.FullSnapshot);
            }

            if (message.PayloadKind == BehaviorTreeNetworkPayloadKind.BlackboardDelta)
            {
                ulong sourceRevision;
                try
                {
                    using (RuntimeBlackboard candidate = CloneBlackboard(
                               tree.Blackboard,
                               EffectiveMaxSnapshotPayloadBytes,
                               _profile.MaxTrackedBlackboardKeys,
                               out sourceRevision))
                    {
                        BTBlackboardDelta.Apply(
                            candidate,
                            new ArraySegment<byte>(message.Payload),
                            _profile.MaxTrackedBlackboardKeys);
                        ulong candidateHash = candidate.ComputeHash(RuntimeBlackboardNetworkScope.Networked);
                        if (candidateHash != message.BlackboardHash ||
                            ComputeLiveTreeStateHash(tree, candidateHash) != message.TreeStateHash)
                        {
                            return RejectIncomingPayload();
                        }
                    }
                }
                catch (InvalidDataException)
                {
                    return RejectIncomingPayload();
                }
                catch (IOException)
                {
                    return RejectIncomingPayload();
                }
                catch (KeyNotFoundException)
                {
                    return RejectIncomingPayload();
                }
                catch (InvalidOperationException)
                {
                    return RejectIncomingPayload();
                }
                catch (ArgumentException)
                {
                    return RejectIncomingPayload();
                }
                catch (OverflowException)
                {
                    return RejectIncomingPayload();
                }

                // Payload parsing, schema validation, and post-state hash validation have
                // completed on the candidate. Commit on the owner blackboard outside the
                // untrusted-payload catch block so application observer failures remain visible.
                try
                {
                    BTBlackboardDelta.Apply(
                        tree.Blackboard,
                        new ArraySegment<byte>(message.Payload),
                        _profile.MaxTrackedBlackboardKeys,
                        sourceRevision);
                }
                catch (InvalidDataException)
                {
                    // Candidate validation cannot observe target-local object slots.
                    // A primitive/object collision is still rejected before mutation.
                    return RejectIncomingPayload();
                }
                ulong committedHash = tree.Blackboard.ComputeHash(RuntimeBlackboardNetworkScope.Networked);
                if (committedHash != message.BlackboardHash ||
                    ComputeLiveTreeStateHash(tree, committedHash) != message.TreeStateHash)
                {
                    throw new InvalidOperationException(
                        "A behavior-tree blackboard delta committed, but application-side mutation changed its validated state hash.");
                }

                if (_profile.WakeTreeOnRemoteDelta)
                {
                    tree.WakeUp();
                }

                receiveState.RecordAccepted(message);
                return AcceptIncomingPayload(BehaviorTreeNetworkPayloadKind.BlackboardDelta);
            }

            if (message.PayloadKind != BehaviorTreeNetworkPayloadKind.HashOnly)
            {
                return RejectIncomingPayload();
            }

            ulong localBlackboardHash = tree.Blackboard.ComputeHash(RuntimeBlackboardNetworkScope.Networked);
            if (localBlackboardHash != message.BlackboardHash ||
                ComputeLiveTreeStateHash(tree, localBlackboardHash) != message.TreeStateHash)
            {
                return RejectIncomingPayload();
            }

            receiveState.RecordAccepted(message);
            return AcceptIncomingPayload(BehaviorTreeNetworkPayloadKind.HashOnly);
        }

        public bool IsDesynced(RuntimeBehaviorTree tree, in BehaviorTreeStatePayloadMessage remoteState)
        {
            EnsureUsable();
            if (tree == null || tree.Blackboard == null || !remoteState.IsValid)
            {
                return true;
            }

            RuntimeBlackboardNetworkScope scope = remoteState.PayloadKind == BehaviorTreeNetworkPayloadKind.FullSnapshot
                ? RuntimeBlackboardNetworkScope.Snapshot
                : RuntimeBlackboardNetworkScope.Networked;
            ulong localBlackboardHash = tree.Blackboard.ComputeHash(scope);
            ulong localTreeStateHash = ComputeLiveTreeStateHash(tree, localBlackboardHash);
            return localBlackboardHash != remoteState.BlackboardHash ||
                   localTreeStateHash != remoteState.TreeStateHash;
        }

        public BehaviorTreeDesyncReportMessage CreateDesyncReport(
            uint targetNetworkId,
            RuntimeBehaviorTree localTree,
            in BehaviorTreeStatePayloadMessage remoteState,
            int localTick,
            ushort sequence)
        {
            EnsureUsable();
            if (targetNetworkId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(targetNetworkId));
            }

            if (localTree == null)
            {
                throw new ArgumentNullException(nameof(localTree));
            }

            RuntimeBlackboardNetworkScope scope = remoteState.PayloadKind == BehaviorTreeNetworkPayloadKind.FullSnapshot
                ? RuntimeBlackboardNetworkScope.Snapshot
                : RuntimeBlackboardNetworkScope.Networked;
            ulong localHash = localTree.Blackboard != null
                ? localTree.Blackboard.ComputeHash(scope)
                : 0UL;
            ulong localTreeStateHash = ComputeLiveTreeStateHash(localTree, localHash);
            return new BehaviorTreeDesyncReportMessage(
                targetNetworkId,
                sequence,
                localTick,
                remoteState.Tick,
                localHash,
                remoteState.BlackboardHash,
                localTreeStateHash,
                remoteState.TreeStateHash,
                remoteState.AuthorityGeneration);
        }

        public void ApplyTickControl(RuntimeBehaviorTree tree, in BehaviorTreeTickControlMessage message)
        {
            EnsureUsable();
            if (tree == null || !message.IsValid)
            {
                return;
            }

            if (message.TickInterval > 0)
            {
                tree.TickInterval = message.TickInterval;
            }

            if ((message.Flags & BehaviorTreeTickControlFlags.Stop) != 0)
            {
                tree.Stop();
            }
            else if ((message.Flags & BehaviorTreeTickControlFlags.Play) != 0)
            {
                tree.Play();
            }

            if ((message.Flags & BehaviorTreeTickControlFlags.WakeUp) != 0)
            {
                tree.WakeUp(message.WakeUpTickBudget);
            }
        }

        private void RecordOutgoingPayload(BehaviorTreeNetworkPayloadKind kind, int payloadBytes)
        {
            switch (kind)
            {
                case BehaviorTreeNetworkPayloadKind.FullSnapshot:
                    _capturedSnapshotCount++;
                    break;
                case BehaviorTreeNetworkPayloadKind.BlackboardDelta:
                    _capturedDeltaCount++;
                    break;
                case BehaviorTreeNetworkPayloadKind.HashOnly:
                    _capturedHashOnlyCount++;
                    break;
            }

            _outgoingPayloadBytes += payloadBytes;
            if (payloadBytes > _peakOutgoingPayloadBytes)
            {
                _peakOutgoingPayloadBytes = payloadBytes;
            }
        }

        private void RecordIncomingPayload(int payloadBytes)
        {
            _receivedPayloadCount++;
            _incomingPayloadBytes += payloadBytes;
            if (payloadBytes > _peakIncomingPayloadBytes)
            {
                _peakIncomingPayloadBytes = payloadBytes;
            }
        }

        private bool RejectIncomingPayload()
        {
            _rejectedPayloadCount++;
            return false;
        }

        private bool AcceptIncomingPayload(BehaviorTreeNetworkPayloadKind kind)
        {
            _acceptedPayloadCount++;
            switch (kind)
            {
                case BehaviorTreeNetworkPayloadKind.FullSnapshot:
                    _acceptedSnapshotCount++;
                    break;
                case BehaviorTreeNetworkPayloadKind.BlackboardDelta:
                    _acceptedDeltaCount++;
                    break;
                case BehaviorTreeNetworkPayloadKind.HashOnly:
                    _acceptedHashOnlyCount++;
                    break;
            }

            return true;
        }

        private bool ValidateIncomingPayload(in BehaviorTreeStatePayloadMessage message)
        {
            if (message.PayloadKind != BehaviorTreeNetworkPayloadKind.FullSnapshot &&
                message.PayloadKind != BehaviorTreeNetworkPayloadKind.BlackboardDelta &&
                message.PayloadKind != BehaviorTreeNetworkPayloadKind.HashOnly)
            {
                return false;
            }

            if (message.PayloadKind == BehaviorTreeNetworkPayloadKind.HashOnly)
            {
                return message.Payload == null || message.Payload.Length == 0;
            }

            if (message.Payload == null || message.Payload.Length == 0)
            {
                return false;
            }

            int maxPayloadSize = message.PayloadKind == BehaviorTreeNetworkPayloadKind.FullSnapshot
                ? EffectiveMaxSnapshotPayloadBytes
                : EffectiveMaxDeltaPayloadBytes;

            return message.Payload.Length <= maxPayloadSize;
        }

        private BTNetworkSync.Limits CreateSnapshotLimits(int maxSnapshotPayloadBytes)
        {
            return new BTNetworkSync.Limits(
                maxSnapshotPayloadBytes,
                BTNetworkSync.Limits.DEFAULT_MAX_NODE_COUNT,
                maxSnapshotPayloadBytes,
                new RuntimeBlackboardSerializationLimits(
                    _profile.MaxTrackedBlackboardKeys,
                    _profile.MaxTrackedBlackboardKeys));
        }

        private static ulong ComputeSnapshotBlackboardHash(
            BTStateSnapshot snapshot,
            RuntimeBlackboard target,
            BTNetworkSync.Limits limits)
        {
            int length = GetSnapshotBlackboardLength(snapshot);
            if (length == 0)
            {
                return 0UL;
            }

            using (var candidate = new RuntimeBlackboard(
                       schema: target.Schema,
                       applySchemaDefaults: false))
            {
                candidate.StringHashFunc = target.StringHashFunc;
                using (var stream = new MemoryStream(snapshot.BlackboardData, 0, length, false))
                using (var reader = new BinaryReader(stream))
                {
                    candidate.ReadFrom(reader, limits.BlackboardLimits);
                    if (stream.Position != stream.Length)
                    {
                        throw new InvalidDataException("Behavior tree snapshot blackboard payload contains trailing bytes.");
                    }
                }

                return candidate.ComputeHash(RuntimeBlackboardNetworkScope.Snapshot);
            }
        }

        private static RuntimeBlackboard CloneBlackboard(
            RuntimeBlackboard source,
            int maxSerializedBytes,
            int maxEntries,
            out ulong sourceRevision)
        {
            var clone = new RuntimeBlackboard(
                schema: source.Schema,
                applySchemaDefaults: false)
            {
                StringHashFunc = source.StringHashFunc
            };

            try
            {
                using (var stream = new MemoryStream(256))
                {
                    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    {
                        sourceRevision = source.WriteTo(
                            writer,
                            RuntimeBlackboardNetworkScope.Networked,
                            maxSerializedBytes);
                        writer.Flush();
                    }

                    stream.Position = 0;
                    using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
                    {
                        clone.ReadFrom(
                            reader,
                            new RuntimeBlackboardSerializationLimits(maxEntries, maxEntries),
                            RuntimeBlackboardNetworkScope.Networked);
                        if (stream.Position != stream.Length)
                        {
                            throw new InvalidDataException("Local blackboard clone contains trailing bytes.");
                        }
                    }
                }

                return clone;
            }
            catch
            {
                clone.Dispose();
                throw;
            }
        }

        private ulong ComputeLiveTreeStateHash(RuntimeBehaviorTree tree, ulong blackboardHash)
        {
            return BTNetworkSync.ComputeTreeStateHash(
                tree,
                blackboardHash,
                _snapshotBuffer,
                CreateSnapshotLimits(EffectiveMaxSnapshotPayloadBytes));
        }

        private static int GetEffectivePayloadBudget(int configuredPayloadBytes)
        {
            return Math.Min(
                configuredPayloadBytes,
                BehaviorTreeNetworkProtocol.DEFAULT_MAX_STATE_MESSAGE_SIZE -
                BehaviorTreeNetworkProtocol.STATE_PAYLOAD_FIXED_ENVELOPE_SIZE);
        }

        private static int GetSnapshotBlackboardLength(BTStateSnapshot snapshot)
        {
            if (snapshot.BlackboardData == null)
            {
                return 0;
            }

            int length = snapshot.BlackboardDataLength > 0
                ? snapshot.BlackboardDataLength
                : snapshot.BlackboardData.Length;
            if (length < 0 || length > snapshot.BlackboardData.Length)
            {
                throw new InvalidDataException("Behavior tree snapshot blackboard length is outside the payload buffer.");
            }

            return length;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            EnsureOwnerThread();
            _disposed = true;
            _snapshotBuffer.Dispose();
        }

        private void EnsureUsable()
        {
            EnsureOwnerThread();
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BehaviorTreeNetworkSyncBridge));
            }
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"BehaviorTreeNetworkSyncBridge must run on owner thread {_ownerThreadId}.");
            }
        }
    }
}
