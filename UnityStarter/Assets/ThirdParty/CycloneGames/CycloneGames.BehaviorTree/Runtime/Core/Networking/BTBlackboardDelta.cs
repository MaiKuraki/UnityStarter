using System;
using System.IO;

namespace CycloneGames.BehaviorTree.Runtime.Core.Networking
{
    /// <summary>
    /// Tracks blackboard key changes between network sync frames.
    /// Only changed keys are transmitted — dramatically reduces bandwidth.
    ///
    /// Usage (Blackboard-Replicated pattern):
    ///   var delta = new BTBlackboardDelta(tree.Blackboard);
    ///   // ... tree ticks, blackboard values change ...
    ///   byte[] patch = delta.Flush();   // only changed keys
    ///   SendToRemote(patch);            // minimal bandwidth
    ///   // Remote side:
    ///   delta.Apply(remoteTree.Blackboard, patch);
    /// </summary>
    public class BTBlackboardDelta
    {
        // Track stamps at last sync point to detect changes
        private readonly int[] _trackedKeys;
        private readonly ulong[] _lastStamps;
        private int _trackedCount;
        private readonly int _maxKeys;

        // Pooled buffers to avoid per-Flush allocation
        private MemoryStream _flushStream;
        private BinaryWriter _flushWriter;

        public BTBlackboardDelta(int maxTrackedKeys = 64)
        {
            _maxKeys = maxTrackedKeys;
            _trackedKeys = new int[maxTrackedKeys];
            _lastStamps = new ulong[maxTrackedKeys];
            _flushStream = new MemoryStream(256);
            _flushWriter = new BinaryWriter(_flushStream);
        }

        /// <summary>
        /// Register a blackboard key for delta tracking.
        /// Call once during setup for each key you want to sync.
        /// </summary>
        public void TrackKey(int keyHash)
        {
            if (_trackedCount >= _maxKeys) return;
            _trackedKeys[_trackedCount] = keyHash;
            _lastStamps[_trackedCount] = 0;
            _trackedCount++;
        }

        public void TrackKey(string key)
        {
            TrackKey(UnityEngine.Animator.StringToHash(key));
        }

        /// <summary>
        /// Flush changed keys into a compact byte[] patch.
        /// Call after tree tick, before sending to network.
        /// Returns null if no changes detected.
        /// </summary>
        public byte[] Flush(RuntimeBlackboard bb)
        {
            // Reuse pooled stream — reset position instead of allocating new
            _flushStream.Position = 0;
            _flushStream.SetLength(0);

            var writer = _flushWriter;
            int changedCount = 0;
            long countPos = _flushStream.Position;
            writer.Write((int)0); // placeholder for count

            for (int i = 0; i < _trackedCount; i++)
            {
                int key = _trackedKeys[i];
                ulong stamp = bb.GetStamp(key);
                if (stamp != _lastStamps[i])
                {
                    _lastStamps[i] = stamp;
                    changedCount++;

                    writer.Write(key);

                    // Write the value type tag + value (precise TryGet — no sentinel ambiguity)
                    if (bb.TryGetInt(key, out var intVal))
                    {
                        writer.Write((byte)0); // int tag
                        writer.Write(intVal);
                    }
                    else if (bb.TryGetFloat(key, out var floatVal))
                    {
                        writer.Write((byte)1); // float tag
                        writer.Write(floatVal);
                    }
                    else if (bb.TryGetBool(key, out var boolVal))
                    {
                        writer.Write((byte)2); // bool tag
                        writer.Write(boolVal ? (byte)1 : (byte)0);
                    }
                    else if (bb.TryGetVector3(key, out var vecVal))
                    {
                        writer.Write((byte)3); // Vector3 tag
                        writer.Write(vecVal.x); writer.Write(vecVal.y); writer.Write(vecVal.z);
                    }
                }
            }

            if (changedCount == 0) return null;

            // Patch the count at the beginning
            writer.Flush();
            _flushStream.Position = countPos;
            writer.Write(changedCount);
            writer.Flush();
            return _flushStream.ToArray();
        }

        /// <summary>
        /// Apply a delta patch to a remote blackboard.
        /// </summary>
        public static void Apply(RuntimeBlackboard bb, byte[] patch)
        {
            if (patch == null || bb == null) return;

            using (var ms = new MemoryStream(patch))
            using (var reader = new BinaryReader(ms))
            {
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int key = reader.ReadInt32();
                    byte tag = reader.ReadByte();

                    switch (tag)
                    {
                        case 0: bb.SetInt(key, reader.ReadInt32()); break;
                        case 1: bb.SetFloat(key, reader.ReadSingle()); break;
                        case 2: bb.SetBool(key, reader.ReadByte() != 0); break;
                        case 3:
                            bb.SetVector3(key, new UnityEngine.Vector3(
                                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
                            break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Deterministic execution helpers for BehaviorTree network sync.
    ///
    /// For client-predicted BT sync to work, execution must be deterministic:
    /// same blackboard inputs at tick N → same tree state at tick N+1.
    ///
    /// Requirements for deterministic BT execution:
    /// 1. No System.Random / UnityEngine.Random in condition/action nodes
    ///    → Use DeterministicRNG with synced seed
    /// 2. No Time.deltaTime in decision logic
    ///    → Use fixed tick count or synced game time
    /// 3. No physics queries (varying between server/client)
    ///    → Write sensor results to blackboard from authoritative source
    /// 4. No floating point differences across platforms
    ///    → Use integer math in conditions; float comparisons with epsilon
    /// </summary>
    public static class BTDeterministic
    {
        /// <summary>
        /// Seedable PRNG for deterministic randomization in BT nodes.
        /// Use instead of UnityEngine.Random for network-synced trees.
        ///
        /// Store the seed in the blackboard for full reproducibility.
        /// </summary>
        public struct DeterministicRNG
        {
            private uint _state;

            public DeterministicRNG(uint seed)
            {
                _state = seed != 0 ? seed : 1;
            }

            /// <summary>xorshift32: fast, deterministic, cross-platform identical.</summary>
            public uint Next()
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return _state;
            }

            /// <summary>Returns [0..max) deterministically.</summary>
            public int NextInt(int max)
            {
                return (int)(Next() % (uint)max);
            }

            /// <summary>Returns [0..1) deterministically.</summary>
            public float NextFloat()
            {
                return (Next() & 0x7FFFFF) / (float)0x800000;
            }

            public uint State => _state;
        }

        /// <summary>
        /// Compare two float values with epsilon tolerance.
        /// Use this instead of == for blackboard float comparisons in networked trees.
        /// </summary>
        public static bool FloatEqual(float a, float b, float epsilon = 0.0001f)
        {
            return Math.Abs(a - b) < epsilon;
        }
    }
}
