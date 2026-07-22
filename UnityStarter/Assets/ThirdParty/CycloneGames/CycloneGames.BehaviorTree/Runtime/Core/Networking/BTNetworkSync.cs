using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.Hash.Core;

namespace CycloneGames.BehaviorTree.Runtime.Core.Networking
{
    /// <summary>
    /// Multiplayer BehaviorTree synchronization system.
    ///
    /// Three recommended sync patterns:
    ///
    /// 1. Server-Authoritative (combat AI, competitive):
    ///    Server runs all trees, captures state, and sends it to clients.
    ///    Clients validate matching managed execution state before synchronizing blackboard values.
    ///    Strength: authoritative decisions. Cost: higher bandwidth and coordinated recovery.
    ///
    /// 2. Client-Predicted (responsive AI, open world):
    ///    Both server and client run BTs independently with same inputs.
    ///    Server sends a state hash; the client compares and starts coordinated recovery on mismatch.
    ///    Strength: low bandwidth and responsiveness. Cost: deterministic inputs and scheduling.
    ///
    /// 3. Blackboard-Replicated (co-op, simple):
    ///    Only replicate blackboard key changes over network.
    ///    Each side runs the tree independently from synchronized inputs.
    ///    Strength: minimal bandwidth. Cost: eventual rather than immediate consistency.
    ///
    /// This class provides tools for all three patterns.
    /// </summary>
    public static class BTNetworkSync
    {
        // "BTS2" in little-endian order. V2 hashes composite execution cursors.
        private const uint SNAPSHOT_MAGIC = 0x32535442u;

        public readonly struct Limits
        {
            public const int DEFAULT_MAX_SNAPSHOT_BYTES = 1024 * 1024;
            public const int DEFAULT_MAX_NODE_COUNT = 65536;
            public const int DEFAULT_MAX_BLACKBOARD_BYTES = 512 * 1024;

            public Limits(
                int maxSnapshotBytes,
                int maxNodeCount,
                int maxBlackboardBytes,
                RuntimeBlackboardSerializationLimits blackboardLimits)
            {
                MaxSnapshotBytes = maxSnapshotBytes > 0 ? maxSnapshotBytes : DEFAULT_MAX_SNAPSHOT_BYTES;
                MaxNodeCount = maxNodeCount > 0 ? maxNodeCount : DEFAULT_MAX_NODE_COUNT;
                MaxBlackboardBytes = maxBlackboardBytes > 0 ? maxBlackboardBytes : DEFAULT_MAX_BLACKBOARD_BYTES;
                BlackboardLimits = blackboardLimits;
            }

            public int MaxSnapshotBytes { get; }
            public int MaxNodeCount { get; }
            public int MaxBlackboardBytes { get; }
            public RuntimeBlackboardSerializationLimits BlackboardLimits { get; }

            public static Limits Default => new Limits(
                DEFAULT_MAX_SNAPSHOT_BYTES,
                DEFAULT_MAX_NODE_COUNT,
                DEFAULT_MAX_BLACKBOARD_BYTES,
                RuntimeBlackboardSerializationLimits.Default);
        }

        /// <summary>
        /// Capture a lightweight snapshot of a tree's execution state.
        /// Contains: all node states + blackboard primitives + tree hash.
        /// </summary>
        public static BTStateSnapshot CaptureSnapshot(RuntimeBehaviorTree tree)
        {
            if (tree?.Root == null)
            {
                return default;
            }

            using (var buffer = new BTStateSnapshotBuffer())
            {
                BTStateSnapshot snapshot = CaptureSnapshot(tree, buffer, Limits.Default);
                return CloneSnapshot(snapshot);
            }
        }

        /// <summary>
        /// Captures into reusable buffers. The returned snapshot references buffer-owned arrays
        /// and must be consumed before the buffer is reused.
        /// </summary>
        public static BTStateSnapshot CaptureSnapshot(RuntimeBehaviorTree tree, BTStateSnapshotBuffer buffer)
        {
            return CaptureSnapshot(tree, buffer, Limits.Default);
        }

        public static BTStateSnapshot CaptureSnapshot(
            RuntimeBehaviorTree tree,
            BTStateSnapshotBuffer buffer,
            Limits limits)
        {
            if (tree?.Root == null || buffer == null)
            {
                return default;
            }

            var snapshot = new BTStateSnapshot();

            buffer.CollectNodes(tree.Root, limits.MaxNodeCount);
            int nodeCount = buffer.TraversalNodes.Count;
            buffer.EnsureNodeCapacity(nodeCount);
            snapshot.NodeStates = buffer.NodeStates;
            snapshot.NodeAuxInts = buffer.NodeAuxInts;
            snapshot.NodeCount = nodeCount;
            for (int i = 0; i < nodeCount; i++)
            {
                RuntimeNode node = buffer.TraversalNodes[i];
                snapshot.NodeStates[i] = (byte)node.State;
                snapshot.NodeAuxInts[i] = node is RuntimeCompositeNode composite
                    ? composite.CurrentIndex
                    : 0;
            }

            if (tree.Blackboard != null)
            {
                buffer.BlackboardStream.SetLength(0);
                tree.Blackboard.WriteTo(
                    buffer.BlackboardWriter,
                    RuntimeBlackboardNetworkScope.Snapshot,
                    limits.MaxBlackboardBytes);
                buffer.BlackboardWriter.Flush();
                snapshot.BlackboardData = buffer.BlackboardStream.GetBuffer();
                snapshot.BlackboardDataLength = (int)buffer.BlackboardStream.Length;
                snapshot.BlackboardHash = tree.Blackboard.ComputeHash(RuntimeBlackboardNetworkScope.Snapshot);
            }

            snapshot.TreeStateHash = ComputeTreeStateHash(snapshot);
            snapshot.Timestamp = RuntimeBTTime.GetUnityTime(false);
            snapshot.IsValid = true;

            return snapshot;
        }

        /// <summary>
        /// Applies the blackboard portion of a validated snapshot.
        /// Managed node execution state is never restored by this method because
        /// private node state cannot be reconstructed generically.
        ///
        /// For full execution-state restore, use the DOD BTTickScheduler execution path,
        /// where mutable agent state has an explicit native-memory owner.
        /// </summary>
        public static void ApplyBlackboardSnapshot(RuntimeBehaviorTree tree, BTStateSnapshot snapshot)
        {
            ApplyBlackboardSnapshot(tree, snapshot, Limits.Default);
        }

        public static void ApplyBlackboardSnapshot(RuntimeBehaviorTree tree, BTStateSnapshot snapshot, Limits limits)
        {
            if (!snapshot.IsValid || tree?.Blackboard == null) return;

            if (snapshot.BlackboardData != null)
            {
                int blackboardLength = GetBlackboardDataLength(snapshot);
                if (blackboardLength > limits.MaxBlackboardBytes)
                {
                    throw new InvalidDataException(
                        $"Blackboard snapshot payload size {blackboardLength} exceeds max blackboard bytes {limits.MaxBlackboardBytes}.");
                }

                using (var ms = new MemoryStream(snapshot.BlackboardData, 0, blackboardLength, false))
                using (var reader = new BinaryReader(ms))
                {
                    tree.Blackboard.ReadFrom(
                        reader,
                        limits.BlackboardLimits,
                        RuntimeBlackboardNetworkScope.Snapshot);
                    if (ms.Position != ms.Length)
                    {
                        throw new InvalidDataException(
                            "Behavior tree snapshot blackboard payload contains trailing bytes.");
                    }
                }
            }
        }

        /// <summary>
        /// Quick desync check: compare local tree hash with server hash.
        /// If mismatch, caller should request full snapshot resync.
        /// </summary>
        public static bool CheckDesync(RuntimeBehaviorTree localTree, ulong serverHash)
        {
            return CheckDesync(localTree, serverHash, RuntimeBlackboardNetworkScope.Networked);
        }

        public static bool CheckDesync(
            RuntimeBehaviorTree localTree,
            ulong serverHash,
            RuntimeBlackboardNetworkScope blackboardScope)
        {
            if (localTree?.Blackboard == null) return true;
            using (var buffer = new BTStateSnapshotBuffer())
            {
                return ComputeTreeStateHash(
                    localTree,
                    blackboardScope,
                    buffer,
                    Limits.Default) != serverHash;
            }
        }

        /// <summary>
        /// Serialize a snapshot to a byte buffer for network transmission.
        /// </summary>
        public static byte[] SerializeSnapshot(BTStateSnapshot snapshot)
        {
            using (var ms = new MemoryStream(512))
            {
                SerializeSnapshot(snapshot, ms);
                return ms.ToArray();
            }
        }

        public static void SerializeSnapshot(BTStateSnapshot snapshot, Stream output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
            {
                WriteSnapshot(snapshot, writer, Limits.Default);
            }
        }

        public static ArraySegment<byte> SerializeSnapshot(BTStateSnapshot snapshot, BTStateSnapshotBuffer buffer)
        {
            return SerializeSnapshot(snapshot, buffer, Limits.Default);
        }

        public static ArraySegment<byte> SerializeSnapshot(
            BTStateSnapshot snapshot,
            BTStateSnapshotBuffer buffer,
            Limits limits)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.SnapshotStream.SetLength(0);
            WriteSnapshot(snapshot, buffer.SnapshotWriter, limits);
            buffer.SnapshotWriter.Flush();
            return new ArraySegment<byte>(buffer.SnapshotStream.GetBuffer(), 0, (int)buffer.SnapshotStream.Length);
        }

        /// <summary>
        /// Deserialize a snapshot from a network byte buffer.
        /// </summary>
        public static BTStateSnapshot DeserializeSnapshot(byte[] data)
        {
            return DeserializeSnapshot(data, Limits.Default);
        }

        public static BTStateSnapshot DeserializeSnapshot(byte[] data, Limits limits)
        {
            if (data == null || data.Length == 0)
            {
                return default;
            }

            if (data.Length > limits.MaxSnapshotBytes)
            {
                throw new InvalidDataException(
                    $"Behavior tree snapshot size {data.Length} exceeds max snapshot bytes {limits.MaxSnapshotBytes}.");
            }

            var snapshot = new BTStateSnapshot();
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                uint magic = reader.ReadUInt32();
                if (magic != SNAPSHOT_MAGIC)
                {
                    throw new InvalidDataException("Behavior tree snapshot payload has an invalid format marker.");
                }

                byte isValid = reader.ReadByte();
                if (isValid > 1)
                {
                    throw new InvalidDataException("Behavior tree snapshot validity flag must be 0 or 1.");
                }

                snapshot.IsValid = isValid != 0;
                snapshot.Timestamp = reader.ReadDouble();
                snapshot.TreeStateHash = reader.ReadUInt64();
                snapshot.BlackboardHash = reader.ReadUInt64();

                int nodeCount = reader.ReadInt32();
                if (nodeCount < 0)
                {
                    throw new InvalidDataException("Behavior tree snapshot node count cannot be negative.");
                }

                if (nodeCount > limits.MaxNodeCount)
                {
                    throw new InvalidDataException(
                        $"Behavior tree snapshot node count {nodeCount} exceeds max node count {limits.MaxNodeCount}.");
                }

                long minimumRemainingBytes = checked(((long)nodeCount * 5L) + sizeof(int));
                if (minimumRemainingBytes > ms.Length - ms.Position)
                {
                    throw new InvalidDataException(
                        "Behavior tree snapshot node count cannot fit in the remaining payload bytes.");
                }

                if (nodeCount > 0)
                {
                    snapshot.NodeStates = ReadExactBytes(reader, nodeCount);
                    for (int i = 0; i < nodeCount; i++)
                    {
                        if (snapshot.NodeStates[i] > (byte)RuntimeState.Failure)
                        {
                            throw new InvalidDataException(
                                $"Behavior tree snapshot node state {snapshot.NodeStates[i]} is invalid.");
                        }
                    }

                    snapshot.NodeAuxInts = new int[nodeCount];
                    for (int i = 0; i < nodeCount; i++)
                        snapshot.NodeAuxInts[i] = reader.ReadInt32();
                    snapshot.NodeCount = nodeCount;
                }

                int bbLen = reader.ReadInt32();
                if (bbLen < 0)
                {
                    throw new InvalidDataException("Behavior tree snapshot blackboard payload length cannot be negative.");
                }

                if (bbLen > limits.MaxBlackboardBytes)
                {
                    throw new InvalidDataException(
                        $"Behavior tree snapshot blackboard payload length {bbLen} exceeds max blackboard bytes {limits.MaxBlackboardBytes}.");
                }

                if (bbLen > ms.Length - ms.Position)
                {
                    throw new InvalidDataException(
                        "Behavior tree snapshot blackboard length exceeds the remaining payload bytes.");
                }

                if (bbLen > 0)
                {
                    snapshot.BlackboardData = ReadExactBytes(reader, bbLen);
                    snapshot.BlackboardDataLength = bbLen;
                }

                if (ms.Position != ms.Length)
                {
                    throw new InvalidDataException("Behavior tree snapshot payload contains trailing bytes.");
                }
            }
            return snapshot;
        }

        private static byte[] ReadExactBytes(BinaryReader reader, int length)
        {
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException(
                    $"Expected {length} bytes but only read {bytes.Length} bytes from behavior tree snapshot.");
            }

            return bytes;
        }

        /// <summary>
        /// Recomputes the deterministic tree-state hash carried by a snapshot.
        /// Call this before accepting snapshot data from an untrusted boundary.
        /// </summary>
        public static ulong ComputeTreeStateHash(BTStateSnapshot snapshot)
        {
            const ulong FNV_OFFSET = Fnv1a64.OffsetBasis;
            const ulong FNV_PRIME = Fnv1a64.Prime;
            ulong hash = FNV_OFFSET;

            if (snapshot.NodeStates != null)
            {
                int nodeCount = GetNodeCount(snapshot);
                for (int i = 0; i < nodeCount; i++)
                {
                    hash = (hash ^ 0xA1u) * FNV_PRIME;
                    hash = (hash ^ snapshot.NodeStates[i]) * FNV_PRIME;
                    int auxiliaryState = snapshot.NodeAuxInts != null && i < snapshot.NodeAuxInts.Length
                        ? snapshot.NodeAuxInts[i]
                        : 0;
                    hash = (hash ^ 0xA2u) * FNV_PRIME;
                    hash = (hash ^ (uint)auxiliaryState) * FNV_PRIME;
                }

            }
            hash = (hash ^ 0xAFu) * FNV_PRIME;
            hash = (hash ^ snapshot.BlackboardHash) * FNV_PRIME;
            return hash;
        }

        /// <summary>
        /// Computes the current node-state and blackboard hash without serializing a snapshot.
        /// The caller selects the blackboard visibility scope explicitly.
        /// </summary>
        public static ulong ComputeTreeStateHash(
            RuntimeBehaviorTree tree,
            RuntimeBlackboardNetworkScope blackboardScope,
            BTStateSnapshotBuffer buffer,
            Limits limits)
        {
            if (tree?.Root == null || tree.Blackboard == null)
            {
                return 0UL;
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            return ComputeTreeStateHash(
                tree,
                tree.Blackboard.ComputeHash(blackboardScope),
                buffer,
                limits);
        }

        public static ulong ComputeTreeStateHash(
            RuntimeBehaviorTree tree,
            ulong blackboardHash,
            BTStateSnapshotBuffer buffer,
            Limits limits)
        {
            if (tree?.Root == null)
            {
                return 0UL;
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.CollectNodes(tree.Root, limits.MaxNodeCount);
            int nodeCount = buffer.TraversalNodes.Count;
            buffer.EnsureNodeCapacity(nodeCount);
            for (int i = 0; i < nodeCount; i++)
            {
                RuntimeNode node = buffer.TraversalNodes[i];
                buffer.NodeStates[i] = (byte)node.State;
                buffer.NodeAuxInts[i] = node is RuntimeCompositeNode composite
                    ? composite.CurrentIndex
                    : 0;
            }

            return ComputeTreeStateHash(new BTStateSnapshot
            {
                NodeStates = buffer.NodeStates,
                NodeAuxInts = buffer.NodeAuxInts,
                NodeCount = nodeCount,
                BlackboardHash = blackboardHash
            });
        }

        private static void WriteSnapshot(BTStateSnapshot snapshot, BinaryWriter writer, Limits limits)
        {
            int nodeCount = GetNodeCount(snapshot);
            int blackboardLength = GetBlackboardDataLength(snapshot);
            ValidateSnapshotWriteBudget(nodeCount, blackboardLength, limits);

            writer.Write(SNAPSHOT_MAGIC);
            writer.Write(snapshot.IsValid);
            writer.Write(snapshot.Timestamp);
            writer.Write(snapshot.TreeStateHash);
            writer.Write(snapshot.BlackboardHash);

            writer.Write(nodeCount);
            if (nodeCount > 0)
            {
                writer.Write(snapshot.NodeStates, 0, nodeCount);
                for (int i = 0; i < nodeCount; i++)
                {
                    int aux = snapshot.NodeAuxInts != null && i < snapshot.NodeAuxInts.Length
                        ? snapshot.NodeAuxInts[i]
                        : 0;
                    writer.Write(aux);
                }
            }

            writer.Write(blackboardLength);
            if (blackboardLength > 0)
            {
                writer.Write(snapshot.BlackboardData, 0, blackboardLength);
            }
        }

        private static BTStateSnapshot CloneSnapshot(BTStateSnapshot snapshot)
        {
            int nodeCount = GetNodeCount(snapshot);
            int blackboardLength = GetBlackboardDataLength(snapshot);

            var clone = snapshot;
            if (nodeCount > 0)
            {
                clone.NodeStates = new byte[nodeCount];
                clone.NodeAuxInts = new int[nodeCount];
                Array.Copy(snapshot.NodeStates, clone.NodeStates, nodeCount);
                Array.Copy(snapshot.NodeAuxInts, clone.NodeAuxInts, nodeCount);
                clone.NodeCount = nodeCount;
            }

            if (blackboardLength > 0)
            {
                clone.BlackboardData = new byte[blackboardLength];
                Buffer.BlockCopy(snapshot.BlackboardData, 0, clone.BlackboardData, 0, blackboardLength);
                clone.BlackboardDataLength = blackboardLength;
            }

            return clone;
        }

        private static int GetNodeCount(BTStateSnapshot snapshot)
        {
            if (snapshot.NodeStates == null)
            {
                return 0;
            }

            int count = snapshot.NodeCount > 0 ? snapshot.NodeCount : snapshot.NodeStates.Length;
            return Math.Min(count, snapshot.NodeStates.Length);
        }

        private static int GetBlackboardDataLength(BTStateSnapshot snapshot)
        {
            if (snapshot.BlackboardData == null)
            {
                return 0;
            }

            int length = snapshot.BlackboardDataLength > 0 ? snapshot.BlackboardDataLength : snapshot.BlackboardData.Length;
            return Math.Min(length, snapshot.BlackboardData.Length);
        }

        private static void ValidateSnapshotWriteBudget(int nodeCount, int blackboardLength, Limits limits)
        {
            if (nodeCount > limits.MaxNodeCount)
            {
                throw new InvalidDataException(
                    $"Behavior tree snapshot node count {nodeCount} exceeds max node count {limits.MaxNodeCount}.");
            }

            if (blackboardLength > limits.MaxBlackboardBytes)
            {
                throw new InvalidDataException(
                    $"Behavior tree snapshot blackboard size {blackboardLength} exceeds max blackboard bytes {limits.MaxBlackboardBytes}.");
            }

            long encodedSize = 37L + (nodeCount * 5L) + blackboardLength;
            if (encodedSize > limits.MaxSnapshotBytes)
            {
                throw new InvalidDataException(
                    $"Behavior tree snapshot size {encodedSize} exceeds max snapshot bytes {limits.MaxSnapshotBytes}.");
            }
        }

        /// <summary>
        /// Compares a snapshot's bounded execution-state projection against the live
        /// managed tree without mutating either object. The projection contains every
        /// node state and each composite node's public execution cursor.
        /// </summary>
        public static bool DoesExecutionStateMatch(
            RuntimeBehaviorTree tree,
            BTStateSnapshot snapshot,
            BTStateSnapshotBuffer buffer,
            Limits limits)
        {
            if (!snapshot.IsValid || tree?.Root == null || buffer == null)
            {
                return false;
            }

            int nodeCount = snapshot.NodeCount;
            if (nodeCount < 0 || nodeCount > limits.MaxNodeCount)
            {
                return false;
            }

            if (nodeCount == 0)
            {
                if ((snapshot.NodeStates != null && snapshot.NodeStates.Length != 0) ||
                    (snapshot.NodeAuxInts != null && snapshot.NodeAuxInts.Length != 0))
                {
                    return false;
                }
            }
            else if (snapshot.NodeStates == null ||
                     snapshot.NodeAuxInts == null ||
                     snapshot.NodeStates.Length < nodeCount ||
                     snapshot.NodeAuxInts.Length < nodeCount)
            {
                return false;
            }

            buffer.CollectNodes(tree.Root, limits.MaxNodeCount);
            if (buffer.TraversalNodes.Count != nodeCount)
            {
                return false;
            }

            for (int i = 0; i < nodeCount; i++)
            {
                RuntimeNode node = buffer.TraversalNodes[i];
                int localAuxiliaryState = node is RuntimeCompositeNode composite
                    ? composite.CurrentIndex
                    : 0;
                if (snapshot.NodeStates[i] != (byte)node.State ||
                    snapshot.NodeAuxInts[i] != localAuxiliaryState)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Immutable snapshot of a BehaviorTree's execution state at a point in time.
    /// Used for network synchronization, replay recording, and state validation.
    /// </summary>
    public struct BTStateSnapshot
    {
        public bool IsValid;
        public double Timestamp;

        /// <summary>Per-node RuntimeState (DFS order matching tree traversal).</summary>
        public byte[] NodeStates;
        /// <summary>Per-node auxiliary int (composite current index, etc.).</summary>
        public int[] NodeAuxInts;
        /// <summary>Number of valid entries in NodeStates and NodeAuxInts.</summary>
        public int NodeCount;

        /// <summary>Serialized blackboard primitive data.</summary>
        public byte[] BlackboardData;
        /// <summary>Number of valid bytes in BlackboardData.</summary>
        public int BlackboardDataLength;

        /// <summary>FNV-1a hash of blackboard data for quick comparison.</summary>
        public ulong BlackboardHash;

        /// <summary>Combined hash of entire tree state (nodes + blackboard).</summary>
        public ulong TreeStateHash;
    }

    /// <summary>
    /// Reusable scratch buffers for high-frequency snapshot capture and serialization.
    /// Instances are not thread-safe.
    /// </summary>
    public sealed class BTStateSnapshotBuffer : IDisposable
    {
        internal readonly MemoryStream BlackboardStream = new MemoryStream(256);
        internal readonly MemoryStream SnapshotStream = new MemoryStream(512);
        internal readonly BinaryWriter BlackboardWriter;
        internal readonly BinaryWriter SnapshotWriter;

        internal byte[] NodeStates = Array.Empty<byte>();
        internal int[] NodeAuxInts = Array.Empty<int>();
        internal readonly List<RuntimeNode> TraversalNodes = new List<RuntimeNode>(64);
        private readonly List<RuntimeNode> _traversalStack = new List<RuntimeNode>(64);
        private bool _disposed;

        public BTStateSnapshotBuffer()
        {
            BlackboardWriter = new BinaryWriter(BlackboardStream, Encoding.UTF8, true);
            SnapshotWriter = new BinaryWriter(SnapshotStream, Encoding.UTF8, true);
        }

        internal void EnsureNodeCapacity(int nodeCount)
        {
            EnsureNotDisposed();
            if (NodeStates.Length < nodeCount)
            {
                NodeStates = new byte[nodeCount];
            }

            if (NodeAuxInts.Length < nodeCount)
            {
                NodeAuxInts = new int[nodeCount];
            }
        }

        internal void CollectNodes(RuntimeNode root, int maxNodeCount)
        {
            EnsureNotDisposed();
            if (maxNodeCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxNodeCount));
            }

            TraversalNodes.Clear();
            _traversalStack.Clear();
            if (root == null)
            {
                return;
            }

            _traversalStack.Add(root);
            while (_traversalStack.Count > 0)
            {
                int last = _traversalStack.Count - 1;
                RuntimeNode node = _traversalStack[last];
                _traversalStack.RemoveAt(last);
                if (node == null)
                {
                    throw new InvalidOperationException("Behavior tree snapshot traversal encountered a null node.");
                }

                if (TraversalNodes.Count >= maxNodeCount)
                {
                    throw new InvalidOperationException(
                        $"Behavior tree snapshot exceeds the node limit of {maxNodeCount}.");
                }

                TraversalNodes.Add(node);
                if (node is RuntimeCompositeNode composite)
                {
                    RuntimeNode[] children = composite.ChildArray;
                    if (children.Length > maxNodeCount - TraversalNodes.Count - _traversalStack.Count)
                    {
                        throw new InvalidOperationException(
                            $"Behavior tree snapshot exceeds the node limit of {maxNodeCount}.");
                    }

                    for (int i = children.Length - 1; i >= 0; i--)
                    {
                        _traversalStack.Add(children[i]);
                    }
                }
                else if (node is Nodes.Decorators.RuntimeDecoratorNode decorator && decorator.Child != null)
                {
                    EnsureTraversalCapacity(maxNodeCount);
                    _traversalStack.Add(decorator.Child);
                }
                else if (node is Nodes.RuntimeRootNode rootNode && rootNode.Child != null)
                {
                    EnsureTraversalCapacity(maxNodeCount);
                    _traversalStack.Add(rootNode.Child);
                }
            }
        }

        private void EnsureTraversalCapacity(int maxNodeCount)
        {
            if (TraversalNodes.Count + _traversalStack.Count >= maxNodeCount)
            {
                throw new InvalidOperationException(
                    $"Behavior tree snapshot exceeds the node limit of {maxNodeCount}.");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            BlackboardWriter.Dispose();
            SnapshotWriter.Dispose();
            BlackboardStream.Dispose();
            SnapshotStream.Dispose();
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BTStateSnapshotBuffer));
            }
        }
    }
}
