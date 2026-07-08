using System;
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
    ///    Server runs all BTs → captures snapshots → sends to clients.
    ///    Client applies snapshots directly (no local BT execution).
    ///    + Cheat-proof, deterministic    − Higher bandwidth, latency
    ///
    /// 2. Client-Predicted (responsive AI, open world):
    ///    Both server and client run BTs independently with same inputs.
    ///    Server sends state hash each tick → client compares → full resync on mismatch.
    ///    + Low bandwidth, responsive    − Requires deterministic execution
    ///
    /// 3. Blackboard-Replicated (co-op, simple):
    ///    Only replicate blackboard key changes over network.
    ///    Each side runs BT independently — same inputs produce same outputs.
    ///    + Minimal bandwidth    − Eventually consistent, not instant
    ///
    /// This class provides tools for all three patterns.
    /// </summary>
    public static class BTNetworkSync
    {
        private const uint SNAPSHOT_MAGIC = 0x31535442u;

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
                BTStateSnapshot snapshot = CaptureSnapshot(tree, buffer);
                return CloneSnapshot(snapshot);
            }
        }

        /// <summary>
        /// Captures into reusable buffers. The returned snapshot references buffer-owned arrays
        /// and must be consumed before the buffer is reused.
        /// </summary>
        public static BTStateSnapshot CaptureSnapshot(RuntimeBehaviorTree tree, BTStateSnapshotBuffer buffer)
        {
            if (tree?.Root == null || buffer == null)
            {
                return default;
            }

            var snapshot = new BTStateSnapshot();

            int nodeCount = CountNodes(tree.Root);
            buffer.EnsureNodeCapacity(nodeCount);
            snapshot.NodeStates = buffer.NodeStates;
            snapshot.NodeAuxInts = buffer.NodeAuxInts;
            snapshot.NodeCount = nodeCount;
            int idx = 0;
            CollectNodeStates(tree.Root, snapshot.NodeStates, snapshot.NodeAuxInts, ref idx);

            if (tree.Blackboard != null)
            {
                buffer.BlackboardStream.SetLength(0);
                tree.Blackboard.WriteTo(buffer.BlackboardWriter);
                buffer.BlackboardWriter.Flush();
                snapshot.BlackboardData = buffer.BlackboardStream.GetBuffer();
                snapshot.BlackboardDataLength = (int)buffer.BlackboardStream.Length;
                snapshot.BlackboardHash = tree.Blackboard.ComputeHash();
            }

            snapshot.TreeStateHash = ComputeTreeStateHash(snapshot);
            snapshot.Timestamp = RuntimeBTTime.GetUnityTime(false);
            snapshot.IsValid = true;

            return snapshot;
        }

        /// <summary>
        /// Apply a snapshot to restore a tree's execution state (client-side).
        /// Restores blackboard data; node execution states are informational only
        /// since managed nodes contain internal mutable state that can't be fully
        /// restored without re-execution.
        ///
        /// For full state restore, use the DOD system (FlatBehaviorTree + BTAgentState)
        /// where all state is external and trivially serializable.
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
                    tree.Blackboard.ReadFrom(reader, limits.BlackboardLimits);
                }
            }
        }

        /// <summary>
        /// Quick desync check: compare local tree hash with server hash.
        /// If mismatch, caller should request full snapshot resync.
        /// </summary>
        public static bool CheckDesync(RuntimeBehaviorTree localTree, ulong serverHash)
        {
            if (localTree?.Blackboard == null) return true;
            return localTree.Blackboard.ComputeHash() != serverHash;
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
                WriteSnapshot(snapshot, writer);
            }
        }

        public static ArraySegment<byte> SerializeSnapshot(BTStateSnapshot snapshot, BTStateSnapshotBuffer buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.SnapshotStream.SetLength(0);
            WriteSnapshot(snapshot, buffer.SnapshotWriter);
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

                snapshot.IsValid = reader.ReadBoolean();
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

                if (nodeCount > 0)
                {
                    snapshot.NodeStates = ReadExactBytes(reader, nodeCount);
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

        // Node counting for snapshot sizing
        private static int CountNodes(RuntimeNode node)
        {
            if (node == null) return 0;
            int count = 1;
            if (node is RuntimeCompositeNode composite && composite.Children != null)
            {
                for (int i = 0; i < composite.Children.Length; i++)
                    count += CountNodes(composite.Children[i]);
            }
            else if (node is Nodes.Decorators.RuntimeDecoratorNode decorator)
            {
                count += CountNodes(decorator.Child);
            }
            else if (node is Nodes.RuntimeRootNode rootNode)
            {
                count += CountNodes(rootNode.Child);
            }
            return count;
        }

        private static void CollectNodeStates(RuntimeNode node, byte[] states, int[] auxInts, ref int idx)
        {
            if (node == null) return;
            states[idx] = (byte)node.State;

            // Store composite's current child index for state-aware sync (0GC via virtual property)
            if (node is RuntimeCompositeNode compositeForIdx)
            {
                auxInts[idx] = compositeForIdx.CurrentIndex;
            }
            idx++;

            if (node is RuntimeCompositeNode composite && composite.Children != null)
            {
                for (int i = 0; i < composite.Children.Length; i++)
                    CollectNodeStates(composite.Children[i], states, auxInts, ref idx);
            }
            else if (node is Nodes.Decorators.RuntimeDecoratorNode decorator)
            {
                CollectNodeStates(decorator.Child, states, auxInts, ref idx);
            }
            else if (node is Nodes.RuntimeRootNode rootNode)
            {
                CollectNodeStates(rootNode.Child, states, auxInts, ref idx);
            }
        }

        private static ulong ComputeTreeStateHash(BTStateSnapshot snapshot)
        {
            const ulong FNV_OFFSET = Fnv1a64.OffsetBasis;
            const ulong FNV_PRIME = Fnv1a64.Prime;
            ulong hash = FNV_OFFSET;

            if (snapshot.NodeStates != null)
            {
                int nodeCount = GetNodeCount(snapshot);
                for (int i = 0; i < nodeCount; i++)
                    hash = (hash ^ snapshot.NodeStates[i]) * FNV_PRIME;
            }
            hash = (hash ^ snapshot.BlackboardHash) * FNV_PRIME;
            return hash;
        }

        private static void WriteSnapshot(BTStateSnapshot snapshot, BinaryWriter writer)
        {
            writer.Write(SNAPSHOT_MAGIC);
            writer.Write(snapshot.IsValid);
            writer.Write(snapshot.Timestamp);
            writer.Write(snapshot.TreeStateHash);
            writer.Write(snapshot.BlackboardHash);

            int nodeCount = GetNodeCount(snapshot);
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

            int blackboardLength = GetBlackboardDataLength(snapshot);
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

        public BTStateSnapshotBuffer()
        {
            BlackboardWriter = new BinaryWriter(BlackboardStream, Encoding.UTF8, true);
            SnapshotWriter = new BinaryWriter(SnapshotStream, Encoding.UTF8, true);
        }

        internal void EnsureNodeCapacity(int nodeCount)
        {
            if (NodeStates.Length < nodeCount)
            {
                NodeStates = new byte[nodeCount];
            }

            if (NodeAuxInts.Length < nodeCount)
            {
                NodeAuxInts = new int[nodeCount];
            }
        }

        public void Dispose()
        {
            BlackboardWriter.Dispose();
            SnapshotWriter.Dispose();
            BlackboardStream.Dispose();
            SnapshotStream.Dispose();
        }
    }
}
