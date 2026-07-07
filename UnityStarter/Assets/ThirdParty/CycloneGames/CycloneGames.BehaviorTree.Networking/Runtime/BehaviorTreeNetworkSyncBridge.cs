using System;
using System.IO;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Networking;

namespace CycloneGames.BehaviorTree.Networking
{
    public sealed class BehaviorTreeNetworkSyncBridge
    {
        private readonly BehaviorTreeNetworkProfile _profile;

        public BehaviorTreeNetworkSyncBridge(BehaviorTreeNetworkProfile profile = null)
        {
            _profile = profile ?? BehaviorTreeNetworkProfiles.ServerAuthoritative;
        }

        public BehaviorTreeNetworkProfile Profile => _profile;

        public BehaviorTreeStatePayloadMessage CaptureSnapshot(
            uint targetNetworkId,
            RuntimeBehaviorTree tree,
            int tick,
            ushort sequence,
            ulong treeTemplateHash = 0UL)
        {
            if (targetNetworkId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(targetNetworkId));
            }

            if (tree == null)
            {
                throw new ArgumentNullException(nameof(tree));
            }

            BTStateSnapshot snapshot = BTNetworkSync.CaptureSnapshot(tree);
            byte[] payload = BTNetworkSync.SerializeSnapshot(snapshot);
            ValidatePayloadSize(payload.Length, _profile.MaxSnapshotPayloadBytes, nameof(CaptureSnapshot));

            return new BehaviorTreeStatePayloadMessage(
                targetNetworkId,
                sequence,
                tick,
                BehaviorTreeNetworkPayloadKind.FullSnapshot,
                BehaviorTreeNetworkProtocol.PROTOCOL_VERSION,
                treeTemplateHash,
                snapshot.BlackboardHash,
                snapshot.TreeStateHash,
                payload);
        }

        public bool TryCreateBlackboardDelta(
            uint targetNetworkId,
            RuntimeBlackboard blackboard,
            BTBlackboardDelta deltaTracker,
            int tick,
            ushort sequence,
            ulong treeTemplateHash,
            out BehaviorTreeStatePayloadMessage message)
        {
            message = default;
            if (targetNetworkId == 0u || blackboard == null || deltaTracker == null)
            {
                return false;
            }

            if (!deltaTracker.TryFlush(blackboard, out ArraySegment<byte> segment))
            {
                return false;
            }

            ValidatePayloadSize(segment.Count, _profile.MaxDeltaPayloadBytes, nameof(TryCreateBlackboardDelta));

            var payload = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array, segment.Offset, payload, 0, segment.Count);

            ulong blackboardHash = blackboard.ComputeHash();
            message = new BehaviorTreeStatePayloadMessage(
                targetNetworkId,
                sequence,
                tick,
                BehaviorTreeNetworkPayloadKind.BlackboardDelta,
                BehaviorTreeNetworkProtocol.PROTOCOL_VERSION,
                treeTemplateHash,
                blackboardHash,
                blackboardHash,
                payload);
            return true;
        }

        public BehaviorTreeStatePayloadMessage CreateHashOnlyMessage(
            uint targetNetworkId,
            RuntimeBehaviorTree tree,
            int tick,
            ushort sequence,
            ulong treeTemplateHash = 0UL)
        {
            if (targetNetworkId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(targetNetworkId));
            }

            if (tree == null)
            {
                throw new ArgumentNullException(nameof(tree));
            }

            ulong blackboardHash = tree.Blackboard != null ? tree.Blackboard.ComputeHash() : 0UL;
            return new BehaviorTreeStatePayloadMessage(
                targetNetworkId,
                sequence,
                tick,
                BehaviorTreeNetworkPayloadKind.HashOnly,
                BehaviorTreeNetworkProtocol.PROTOCOL_VERSION,
                treeTemplateHash,
                blackboardHash,
                blackboardHash,
                null);
        }

        public bool ApplyPayload(RuntimeBehaviorTree tree, in BehaviorTreeStatePayloadMessage message)
        {
            if (tree == null || !message.IsValid)
            {
                return false;
            }

            if (!ValidateIncomingPayload(message))
            {
                return false;
            }

            if (message.PayloadKind == BehaviorTreeNetworkPayloadKind.FullSnapshot)
            {
                try
                {
                    BTNetworkSync.Limits limits = CreateSnapshotLimits(_profile.MaxSnapshotPayloadBytes);
                    BTStateSnapshot snapshot = BTNetworkSync.DeserializeSnapshot(message.Payload, limits);
                    BTNetworkSync.ApplyBlackboardSnapshot(tree, snapshot, limits);
                }
                catch (InvalidDataException)
                {
                    return false;
                }
                catch (EndOfStreamException)
                {
                    return false;
                }

                if (_profile.WakeTreeOnRemoteDelta)
                {
                    tree.WakeUp();
                }

                return true;
            }

            if (message.PayloadKind == BehaviorTreeNetworkPayloadKind.BlackboardDelta)
            {
                try
                {
                    BTBlackboardDelta.Apply(tree.Blackboard, message.Payload);
                }
                catch (InvalidDataException)
                {
                    return false;
                }
                catch (EndOfStreamException)
                {
                    return false;
                }

                if (_profile.WakeTreeOnRemoteDelta)
                {
                    tree.WakeUp();
                }

                return true;
            }

            return message.PayloadKind == BehaviorTreeNetworkPayloadKind.HashOnly;
        }

        public bool IsDesynced(RuntimeBehaviorTree tree, in BehaviorTreeStatePayloadMessage remoteState)
        {
            if (tree == null || tree.Blackboard == null || !remoteState.IsValid)
            {
                return true;
            }

            return tree.Blackboard.ComputeHash() != remoteState.BlackboardHash;
        }

        public BehaviorTreeDesyncReportMessage CreateDesyncReport(
            uint targetNetworkId,
            RuntimeBehaviorTree localTree,
            in BehaviorTreeStatePayloadMessage remoteState,
            int localTick,
            ushort sequence)
        {
            if (targetNetworkId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(targetNetworkId));
            }

            if (localTree == null)
            {
                throw new ArgumentNullException(nameof(localTree));
            }

            ulong localHash = localTree.Blackboard != null ? localTree.Blackboard.ComputeHash() : 0UL;
            return new BehaviorTreeDesyncReportMessage(
                targetNetworkId,
                sequence,
                localTick,
                remoteState.Tick,
                localHash,
                remoteState.BlackboardHash,
                localHash,
                remoteState.TreeStateHash);
        }

        public void ApplyTickControl(RuntimeBehaviorTree tree, in BehaviorTreeTickControlMessage message)
        {
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

        private static void ValidatePayloadSize(int payloadSize, int maxPayloadSize, string operationName)
        {
            if (payloadSize > maxPayloadSize)
            {
                throw new InvalidOperationException($"{operationName} payload size {payloadSize} exceeds max payload size {maxPayloadSize}.");
            }
        }

        private bool ValidateIncomingPayload(in BehaviorTreeStatePayloadMessage message)
        {
            if (message.PayloadKind == BehaviorTreeNetworkPayloadKind.HashOnly)
            {
                return true;
            }

            if (message.Payload == null || message.Payload.Length == 0)
            {
                return false;
            }

            int maxPayloadSize = message.PayloadKind == BehaviorTreeNetworkPayloadKind.FullSnapshot
                ? _profile.MaxSnapshotPayloadBytes
                : _profile.MaxDeltaPayloadBytes;

            return message.Payload.Length <= maxPayloadSize;
        }

        private static BTNetworkSync.Limits CreateSnapshotLimits(int maxSnapshotPayloadBytes)
        {
            return new BTNetworkSync.Limits(
                maxSnapshotPayloadBytes,
                BTNetworkSync.Limits.DEFAULT_MAX_NODE_COUNT,
                maxSnapshotPayloadBytes,
                RuntimeBlackboardSerializationLimits.Default);
        }
    }
}
