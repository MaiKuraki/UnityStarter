using System;
using System.IO;

namespace CycloneGames.BehaviorTree.Runtime.Core.Networking
{
    /// <summary>
    /// Tracks blackboard key changes between network sync frames.
    /// Only changed keys are transmitted.
    /// </summary>
    public class BTBlackboardDelta
    {
        private readonly int[] _trackedKeys;
        private readonly ulong[] _lastStamps;
        private int _trackedCount;
        private readonly int _maxKeys;

        // Pooled buffers to avoid per-flush allocations in the hot path.
        private readonly MemoryStream _flushStream;
        private readonly BinaryWriter _flushWriter;

        public BTBlackboardDelta(int maxTrackedKeys = 64)
        {
            _maxKeys = maxTrackedKeys;
            _trackedKeys = new int[maxTrackedKeys];
            _lastStamps = new ulong[maxTrackedKeys];
            _flushStream = new MemoryStream(256);
            _flushWriter = new BinaryWriter(_flushStream);
        }

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
        /// Compatibility API. Returns a right-sized copy and therefore allocates.
        /// Prefer <see cref="TryFlush(RuntimeBlackboard, out ArraySegment{byte})"/> in hot paths.
        /// </summary>
        public byte[] Flush(RuntimeBlackboard bb)
        {
            if (!TryFlush(bb, out var patchSegment))
            {
                return null;
            }

            var result = new byte[patchSegment.Count];
            Buffer.BlockCopy(patchSegment.Array, patchSegment.Offset, result, 0, patchSegment.Count);
            return result;
        }

        /// <summary>
        /// Non-alloc flush API. The returned segment points to this instance's
        /// internal pooled buffer and stays valid until the next flush call.
        /// </summary>
        public bool TryFlush(RuntimeBlackboard bb, out ArraySegment<byte> patchSegment)
        {
            _flushStream.Position = 0;
            _flushStream.SetLength(0);

            int changedCount = 0;
            long countPos = _flushStream.Position;
            _flushWriter.Write(0); // placeholder for count

            for (int i = 0; i < _trackedCount; i++)
            {
                int key = _trackedKeys[i];
                ulong stamp = bb.GetStamp(key);
                if (stamp == _lastStamps[i]) continue;

                _lastStamps[i] = stamp;
                changedCount++;

                _flushWriter.Write(key);

                if (bb.TryGetInt(key, out var intVal))
                {
                    _flushWriter.Write((byte)0);
                    _flushWriter.Write(intVal);
                }
                else if (bb.TryGetFloat(key, out var floatVal))
                {
                    _flushWriter.Write((byte)1);
                    _flushWriter.Write(floatVal);
                }
                else if (bb.TryGetBool(key, out var boolVal))
                {
                    _flushWriter.Write((byte)2);
                    _flushWriter.Write(boolVal ? (byte)1 : (byte)0);
                }
                else if (bb.TryGetVector3(key, out var vecVal))
                {
                    _flushWriter.Write((byte)3);
                    _flushWriter.Write(vecVal.x);
                    _flushWriter.Write(vecVal.y);
                    _flushWriter.Write(vecVal.z);
                }
                else
                {
                    // Key was removed or now holds an unsupported type.
                    _flushWriter.Write((byte)255);
                }
            }

            if (changedCount == 0)
            {
                patchSegment = default;
                return false;
            }

            _flushWriter.Flush();
            _flushStream.Position = countPos;
            _flushWriter.Write(changedCount);
            _flushWriter.Flush();

            patchSegment = new ArraySegment<byte>(_flushStream.GetBuffer(), 0, (int)_flushStream.Length);
            return true;
        }

        public static void Apply(RuntimeBlackboard bb, byte[] patch)
        {
            if (patch == null || bb == null) return;
            Apply(bb, new ArraySegment<byte>(patch, 0, patch.Length));
        }

        public static void Apply(RuntimeBlackboard bb, ArraySegment<byte> patch)
        {
            if (patch.Array == null || bb == null || patch.Count <= 0) return;

            using (var ms = new MemoryStream(patch.Array, patch.Offset, patch.Count, false, true))
            using (var reader = new BinaryReader(ms))
            {
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int key = reader.ReadInt32();
                    byte tag = reader.ReadByte();

                    switch (tag)
                    {
                        case 0:
                            bb.SetInt(key, reader.ReadInt32());
                            break;
                        case 1:
                            bb.SetFloat(key, reader.ReadSingle());
                            break;
                        case 2:
                            bb.SetBool(key, reader.ReadByte() != 0);
                            break;
                        case 3:
                            bb.SetVector3(key, new UnityEngine.Vector3(
                                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
                            break;
                        case 255:
                            bb.Remove(key);
                            break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Deterministic execution helpers for BehaviorTree network sync.
    /// </summary>
    public static class BTDeterministic
    {
        public struct DeterministicRNG
        {
            private uint _state;

            public DeterministicRNG(uint seed)
            {
                _state = seed != 0 ? seed : 1;
            }

            public uint Next()
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return _state;
            }

            public int NextInt(int max)
            {
                return (int)(Next() % (uint)max);
            }

            public float NextFloat()
            {
                return (Next() & 0x7FFFFF) / (float)0x800000;
            }

            public float Range(float minInclusive, float maxInclusive)
            {
                return minInclusive + (maxInclusive - minInclusive) * NextFloat();
            }

            public uint State => _state;
        }

        public static bool FloatEqual(float a, float b, float epsilon = 0.0001f)
        {
            return Math.Abs(a - b) < epsilon;
        }
    }
}
