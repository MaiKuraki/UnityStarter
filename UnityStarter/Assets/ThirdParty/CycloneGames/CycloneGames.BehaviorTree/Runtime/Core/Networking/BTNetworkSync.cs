using System;
using System.IO;
using CycloneGames.BehaviorTree.Runtime.Core;

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
        /// <summary>
        /// Capture a lightweight snapshot of a tree's execution state.
        /// Contains: all node states + blackboard primitives + tree hash.
        /// </summary>
        public static BTStateSnapshot CaptureSnapshot(RuntimeBehaviorTree tree)
        {
            if (tree?.Root == null) return default;

            var snapshot = new BTStateSnapshot();

            // Collect node states via DFS
            int nodeCount = CountNodes(tree.Root);
            snapshot.NodeStates = new byte[nodeCount];
            snapshot.NodeAuxInts = new int[nodeCount];
            int idx = 0;
            CollectNodeStates(tree.Root, snapshot.NodeStates, snapshot.NodeAuxInts, ref idx);

            // Serialize blackboard
            if (tree.Blackboard != null)
            {
                using (var ms = new MemoryStream(256))
                using (var writer = new BinaryWriter(ms))
                {
                    tree.Blackboard.WriteTo(writer);
                    snapshot.BlackboardData = ms.ToArray();
                }
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
            if (!snapshot.IsValid || tree?.Blackboard == null) return;

            if (snapshot.BlackboardData != null)
            {
                using (var ms = new MemoryStream(snapshot.BlackboardData))
                using (var reader = new BinaryReader(ms))
                {
                    tree.Blackboard.ReadFrom(reader);
                }
            }
        }

        /// <summary>
        /// Quick desync check: compare local tree hash with server hash.
        /// If mismatch, caller should request full snapshot resync.
        /// </summary>
        public static bool CheckDesync(RuntimeBehaviorTree localTree, uint serverHash)
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
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(snapshot.IsValid);
                writer.Write(snapshot.Timestamp);
                writer.Write(snapshot.TreeStateHash);
                writer.Write(snapshot.BlackboardHash);

                writer.Write(snapshot.NodeStates?.Length ?? 0);
                if (snapshot.NodeStates != null)
                {
                    writer.Write(snapshot.NodeStates);
                    for (int i = 0; i < snapshot.NodeAuxInts.Length; i++)
                        writer.Write(snapshot.NodeAuxInts[i]);
                }

                writer.Write(snapshot.BlackboardData?.Length ?? 0);
                if (snapshot.BlackboardData != null)
                    writer.Write(snapshot.BlackboardData);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserialize a snapshot from a network byte buffer.
        /// </summary>
        public static BTStateSnapshot DeserializeSnapshot(byte[] data)
        {
            var snapshot = new BTStateSnapshot();
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                snapshot.IsValid = reader.ReadBoolean();
                snapshot.Timestamp = reader.ReadDouble();
                snapshot.TreeStateHash = reader.ReadUInt32();
                snapshot.BlackboardHash = reader.ReadUInt32();

                int nodeCount = reader.ReadInt32();
                if (nodeCount > 0)
                {
                    snapshot.NodeStates = reader.ReadBytes(nodeCount);
                    snapshot.NodeAuxInts = new int[nodeCount];
                    for (int i = 0; i < nodeCount; i++)
                        snapshot.NodeAuxInts[i] = reader.ReadInt32();
                }

                int bbLen = reader.ReadInt32();
                if (bbLen > 0)
                    snapshot.BlackboardData = reader.ReadBytes(bbLen);
            }
            return snapshot;
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

        private static uint ComputeTreeStateHash(BTStateSnapshot snapshot)
        {
            const uint FNV_OFFSET = 2166136261u;
            const uint FNV_PRIME = 16777619u;
            uint hash = FNV_OFFSET;

            if (snapshot.NodeStates != null)
            {
                for (int i = 0; i < snapshot.NodeStates.Length; i++)
                    hash = (hash ^ snapshot.NodeStates[i]) * FNV_PRIME;
            }
            hash = (hash ^ snapshot.BlackboardHash) * FNV_PRIME;
            return hash;
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

        /// <summary>Serialized blackboard primitive data.</summary>
        public byte[] BlackboardData;

        /// <summary>FNV-1a hash of blackboard data for quick comparison.</summary>
        public uint BlackboardHash;

        /// <summary>Combined hash of entire tree state (nodes + blackboard).</summary>
        public uint TreeStateHash;
    }
}
