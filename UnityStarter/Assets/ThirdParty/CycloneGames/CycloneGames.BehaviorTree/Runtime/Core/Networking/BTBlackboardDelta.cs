using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace CycloneGames.BehaviorTree.Runtime.Core.Networking
{
    /// <summary>
    /// Tracks blackboard key changes between network sync frames.
    /// Only changed keys are transmitted.
    /// </summary>
    public class BTBlackboardDelta : IDisposable
    {
        public const int DEFAULT_MAX_PATCH_ENTRIES = 4096;

        private const byte TAG_INT = 0;
        private const byte TAG_FLOAT = 1;
        private const byte TAG_BOOL = 2;
        private const byte TAG_VECTOR3 = 3;
        private const byte TAG_LONG = 4;
        private const byte TAG_LONG2 = 5;
        private const byte TAG_LONG3 = 6;
        private const byte TAG_REMOVE = 255;

        private readonly int[] _trackedKeys;
        private readonly ulong[] _lastStamps;
        private readonly Dictionary<int, int> _trackedIndexByKey;
        private readonly int[] _dirtyIndices;
        private readonly bool[] _dirtyFlags;
        private readonly BlackboardObserverCallback _dirtyObserver;
        private int _trackedCount;
        private int _dirtyCount;
        private readonly int _maxKeys;
        private RuntimeBlackboard _attachedBlackboard;
        private bool _disposed;

        // Pooled buffers to avoid per-flush allocations in the hot path.
        private readonly MemoryStream _flushStream;
        private readonly BinaryWriter _flushWriter;

        public BTBlackboardDelta(int maxTrackedKeys = 64)
        {
            if (maxTrackedKeys <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTrackedKeys), "Delta tracker capacity must be greater than zero.");
            }

            _maxKeys = maxTrackedKeys;
            _trackedKeys = new int[maxTrackedKeys];
            _lastStamps = new ulong[maxTrackedKeys];
            _trackedIndexByKey = new Dictionary<int, int>(maxTrackedKeys);
            _dirtyIndices = new int[maxTrackedKeys];
            _dirtyFlags = new bool[maxTrackedKeys];
            _dirtyObserver = OnTrackedKeyChanged;
            _flushStream = new MemoryStream(256);
            _flushWriter = new BinaryWriter(_flushStream);
        }

        public static BTBlackboardDelta CreateForSchema(RuntimeBlackboardSchema schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            var delta = new BTBlackboardDelta(Math.Max(1, schema.DeltaKeyCount));
            for (int i = 0; i < schema.DeltaKeyCount; i++)
            {
                delta.TrackKey(schema.GetDeltaKey(i));
            }

            return delta;
        }

        public void TrackKey(int keyHash)
        {
            EnsureNotDisposed();

            if (_trackedIndexByKey.ContainsKey(keyHash))
            {
                return;
            }

            if (_trackedCount >= _maxKeys)
            {
                throw new InvalidOperationException($"Cannot track more than {_maxKeys} blackboard delta keys.");
            }

            int index = _trackedCount;
            _trackedKeys[_trackedCount] = keyHash;
            _lastStamps[_trackedCount] = 0;
            _trackedIndexByKey[keyHash] = index;
            _trackedCount++;

            if (_attachedBlackboard != null)
            {
                _attachedBlackboard.AddObserver(keyHash, _dirtyObserver);
                MarkDirty(index);
            }
        }

        public void TrackKey(string key)
        {
            TrackKey(UnityEngine.Animator.StringToHash(key));
        }

        /// <summary>
        /// Binds this delta tracker to a blackboard observer path.
        /// When attached to the same blackboard passed to TryFlush, only dirty tracked keys are probed.
        /// </summary>
        public void Attach(RuntimeBlackboard blackboard, bool flushExistingValues = true)
        {
            EnsureNotDisposed();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            if (_attachedBlackboard == blackboard)
            {
                if (flushExistingValues)
                {
                    MarkAllTrackedDirty();
                }

                return;
            }

            Detach();
            _attachedBlackboard = blackboard;
            for (int i = 0; i < _trackedCount; i++)
            {
                _attachedBlackboard.AddObserver(_trackedKeys[i], _dirtyObserver);
            }

            if (flushExistingValues)
            {
                MarkAllTrackedDirty();
            }
        }

        public void Detach()
        {
            if (_disposed)
            {
                return;
            }

            RuntimeBlackboard blackboard = _attachedBlackboard;
            if (blackboard != null)
            {
                for (int i = 0; i < _trackedCount; i++)
                {
                    blackboard.RemoveObserver(_trackedKeys[i], _dirtyObserver);
                }
            }

            _attachedBlackboard = null;
            ClearDirty();
        }

        /// <summary>
        /// Non-alloc flush API. The returned segment points to this instance's
        /// internal pooled buffer and stays valid until the next flush call.
        /// </summary>
        public bool TryFlush(RuntimeBlackboard bb, out ArraySegment<byte> patchSegment)
        {
            EnsureNotDisposed();

            if (bb == null)
            {
                throw new ArgumentNullException(nameof(bb));
            }

            if (_attachedBlackboard == bb)
            {
                return TryFlushDirty(bb, out patchSegment);
            }

            return TryFlushTrackedScan(bb, out patchSegment);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Detach();
            _disposed = true;
            _flushWriter.Dispose();
            _flushStream.Dispose();
        }

        private bool TryFlushDirty(RuntimeBlackboard bb, out ArraySegment<byte> patchSegment)
        {
            if (_dirtyCount == 0)
            {
                patchSegment = default;
                return false;
            }

            BeginPatch(out long countPos);

            int changedCount = 0;
            int dirtyCount = _dirtyCount;
            _dirtyCount = 0;

            for (int i = 0; i < dirtyCount; i++)
            {
                int trackedIndex = _dirtyIndices[i];
                _dirtyFlags[trackedIndex] = false;
                if (WriteTrackedKeyIfChanged(bb, trackedIndex))
                {
                    changedCount++;
                }
            }

            return CompletePatch(countPos, changedCount, out patchSegment);
        }

        private bool TryFlushTrackedScan(RuntimeBlackboard bb, out ArraySegment<byte> patchSegment)
        {
            BeginPatch(out long countPos);

            int changedCount = 0;
            for (int i = 0; i < _trackedCount; i++)
            {
                if (WriteTrackedKeyIfChanged(bb, i))
                {
                    changedCount++;
                }
            }

            return CompletePatch(countPos, changedCount, out patchSegment);
        }

        private void BeginPatch(out long countPos)
        {
            _flushStream.Position = 0;
            _flushStream.SetLength(0);
            countPos = _flushStream.Position;
            _flushWriter.Write(0);
        }

        private bool CompletePatch(long countPos, int changedCount, out ArraySegment<byte> patchSegment)
        {
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

        private bool WriteTrackedKeyIfChanged(RuntimeBlackboard bb, int trackedIndex)
        {
            int key = _trackedKeys[trackedIndex];
            ulong stamp = bb.GetStamp(key);
            if (stamp == _lastStamps[trackedIndex])
            {
                return false;
            }

            _lastStamps[trackedIndex] = stamp;

            _flushWriter.Write(key);

            if (bb.TryGetInt(key, out var intVal))
            {
                _flushWriter.Write(TAG_INT);
                _flushWriter.Write(intVal);
            }
            else if (bb.TryGetFloat(key, out var floatVal))
            {
                _flushWriter.Write(TAG_FLOAT);
                _flushWriter.Write(floatVal);
            }
            else if (bb.TryGetBool(key, out var boolVal))
            {
                _flushWriter.Write(TAG_BOOL);
                _flushWriter.Write(boolVal ? (byte)1 : (byte)0);
            }
            else if (bb.TryGetVector3(key, out var vecVal))
            {
                _flushWriter.Write(TAG_VECTOR3);
                _flushWriter.Write(vecVal.x);
                _flushWriter.Write(vecVal.y);
                _flushWriter.Write(vecVal.z);
            }
            else if (bb.TryGetLong(key, out long longVal))
            {
                _flushWriter.Write(TAG_LONG);
                _flushWriter.Write(longVal);
            }
            else if (bb.TryGetLong2(key, out RuntimeBlackboardLong2 long2Val))
            {
                _flushWriter.Write(TAG_LONG2);
                _flushWriter.Write(long2Val.X);
                _flushWriter.Write(long2Val.Y);
            }
            else if (bb.TryGetLong3(key, out RuntimeBlackboardLong3 long3Val))
            {
                _flushWriter.Write(TAG_LONG3);
                _flushWriter.Write(long3Val.X);
                _flushWriter.Write(long3Val.Y);
                _flushWriter.Write(long3Val.Z);
            }
            else
            {
                _flushWriter.Write(TAG_REMOVE);
            }

            return true;
        }

        private void OnTrackedKeyChanged(int keyHash, RuntimeBlackboard blackboard)
        {
            if (_trackedIndexByKey.TryGetValue(keyHash, out int trackedIndex))
            {
                MarkDirty(trackedIndex);
            }
        }

        private void MarkAllTrackedDirty()
        {
            for (int i = 0; i < _trackedCount; i++)
            {
                MarkDirty(i);
            }
        }

        private void MarkDirty(int trackedIndex)
        {
            if (_dirtyFlags[trackedIndex])
            {
                return;
            }

            _dirtyFlags[trackedIndex] = true;
            _dirtyIndices[_dirtyCount] = trackedIndex;
            _dirtyCount++;
        }

        private void ClearDirty()
        {
            for (int i = 0; i < _dirtyCount; i++)
            {
                _dirtyFlags[_dirtyIndices[i]] = false;
            }

            _dirtyCount = 0;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BTBlackboardDelta));
            }
        }

        public static void Apply(RuntimeBlackboard bb, byte[] patch)
        {
            Apply(bb, patch, DEFAULT_MAX_PATCH_ENTRIES);
        }

        public static void Apply(RuntimeBlackboard bb, byte[] patch, int maxPatchEntries)
        {
            if (patch == null || bb == null)
            {
                return;
            }

            Apply(bb, new ArraySegment<byte>(patch, 0, patch.Length), maxPatchEntries);
        }

        public static void Apply(RuntimeBlackboard bb, ArraySegment<byte> patch)
        {
            Apply(bb, patch, DEFAULT_MAX_PATCH_ENTRIES);
        }

        public static void Apply(RuntimeBlackboard bb, ArraySegment<byte> patch, int maxPatchEntries)
        {
            if (patch.Array == null || bb == null || patch.Count <= 0)
            {
                return;
            }

            if (patch.Offset < 0 || patch.Count < 0 || patch.Offset > patch.Array.Length - patch.Count)
            {
                throw new InvalidDataException("Blackboard delta payload segment is outside the source buffer.");
            }

            var reader = new DeltaPayloadReader(patch.Array, patch.Offset, patch.Count);
            int count = reader.ReadInt32();
            if (count < 0)
            {
                throw new InvalidDataException("Blackboard delta entry count cannot be negative.");
            }

            if (count > maxPatchEntries)
            {
                throw new InvalidDataException(
                    $"Blackboard delta entry count {count} exceeds max patch entries {maxPatchEntries}.");
            }

            DeltaEntry[] entries = count > 0
                ? ArrayPool<DeltaEntry>.Shared.Rent(count)
                : Array.Empty<DeltaEntry>();

            try
            {
                for (int i = 0; i < count; i++)
                {
                    int key = reader.ReadInt32();
                    byte tag = reader.ReadByte();
                    entries[i] = new DeltaEntry(key, tag);

                    switch (tag)
                    {
                        case TAG_INT:
                            entries[i].IntValue = reader.ReadInt32();
                            break;
                        case TAG_FLOAT:
                            entries[i].FloatValue = reader.ReadSingle();
                            break;
                        case TAG_BOOL:
                            entries[i].BoolValue = reader.ReadByte() != 0;
                            break;
                        case TAG_VECTOR3:
                            entries[i].VectorValue = new UnityEngine.Vector3(
                                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            break;
                        case TAG_LONG:
                            entries[i].LongValue = reader.ReadInt64();
                            break;
                        case TAG_LONG2:
                            entries[i].Long2Value = new RuntimeBlackboardLong2(reader.ReadInt64(), reader.ReadInt64());
                            break;
                        case TAG_LONG3:
                            entries[i].Long3Value = new RuntimeBlackboardLong3(
                                reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());
                            break;
                        case TAG_REMOVE:
                            break;
                        default:
                            throw new InvalidDataException($"Unknown blackboard delta value tag {tag}.");
                    }
                }

                if (!reader.IsComplete)
                {
                    throw new InvalidDataException("Blackboard delta payload contains trailing bytes.");
                }

                for (int i = 0; i < count; i++)
                {
                    entries[i].Apply(bb);
                }
            }
            finally
            {
                if (count > 0)
                {
                    ArrayPool<DeltaEntry>.Shared.Return(entries);
                }
            }
        }

        private struct DeltaEntry
        {
            public readonly int Key;
            public readonly byte Tag;
            public int IntValue;
            public float FloatValue;
            public bool BoolValue;
            public UnityEngine.Vector3 VectorValue;
            public long LongValue;
            public RuntimeBlackboardLong2 Long2Value;
            public RuntimeBlackboardLong3 Long3Value;

            public DeltaEntry(int key, byte tag)
            {
                Key = key;
                Tag = tag;
                IntValue = default;
                FloatValue = default;
                BoolValue = default;
                VectorValue = default;
                LongValue = default;
                Long2Value = default;
                Long3Value = default;
            }

            public void Apply(RuntimeBlackboard blackboard)
            {
                switch (Tag)
                {
                    case TAG_INT:
                        blackboard.SetInt(Key, IntValue);
                        break;
                    case TAG_FLOAT:
                        blackboard.SetFloat(Key, FloatValue);
                        break;
                    case TAG_BOOL:
                        blackboard.SetBool(Key, BoolValue);
                        break;
                    case TAG_VECTOR3:
                        blackboard.SetVector3(Key, VectorValue);
                        break;
                    case TAG_LONG:
                        blackboard.SetLong(Key, LongValue);
                        break;
                    case TAG_LONG2:
                        blackboard.SetLong2(Key, Long2Value);
                        break;
                    case TAG_LONG3:
                        blackboard.SetLong3(Key, Long3Value);
                        break;
                    case TAG_REMOVE:
                        blackboard.Remove(Key);
                        break;
                }
            }
        }

        private struct DeltaPayloadReader
        {
            private readonly byte[] _buffer;
            private readonly int _end;
            private int _position;

            public DeltaPayloadReader(byte[] buffer, int offset, int count)
            {
                _buffer = buffer;
                _position = offset;
                _end = offset + count;
            }

            public bool IsComplete => _position == _end;

            public byte ReadByte()
            {
                EnsureAvailable(1);
                return _buffer[_position++];
            }

            public int ReadInt32()
            {
                EnsureAvailable(4);
                int position = _position;
                _position += 4;
                return _buffer[position]
                    | (_buffer[position + 1] << 8)
                    | (_buffer[position + 2] << 16)
                    | (_buffer[position + 3] << 24);
            }

            public long ReadInt64()
            {
                ulong low = unchecked((uint)ReadInt32());
                ulong high = unchecked((uint)ReadInt32());
                return unchecked((long)(low | (high << 32)));
            }

            public float ReadSingle()
            {
                var union = new FloatIntUnion
                {
                    IntValue = ReadInt32()
                };
                return union.FloatValue;
            }

            private void EnsureAvailable(int byteCount)
            {
                if (_position < 0 || _position + byteCount > _end)
                {
                    throw new EndOfStreamException("Blackboard delta payload ended before the declared entry data was fully read.");
                }
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct FloatIntUnion
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public int IntValue;
            [System.Runtime.InteropServices.FieldOffset(0)] public float FloatValue;
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
