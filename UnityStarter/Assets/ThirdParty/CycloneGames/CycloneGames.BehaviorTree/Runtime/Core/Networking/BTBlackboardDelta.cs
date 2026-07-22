using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CycloneGames.BehaviorTree.Runtime.Core.Networking
{
    /// <summary>
    /// Tracks blackboard key changes between network sync frames.
    /// Only changed keys are transmitted.
    /// </summary>
    public class BTBlackboardDelta : IDisposable
    {
        public const int DEFAULT_MAX_PATCH_ENTRIES = 4096;

        // "BTDP" in little-endian order. The version and fixed header size are
        // encoded separately so malformed or future frames fail before entry parsing.
        private const uint PATCH_MAGIC = 0x50445442u;
        private const ushort PATCH_VERSION = 1;
        private const ushort PATCH_HEADER_SIZE = 16;
        private const int MIN_ENCODED_ENTRY_SIZE = sizeof(int) + sizeof(byte);

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
        private readonly RuntimeBlackboardMutation[] _candidateMutations;
        private readonly int[] _candidateTrackedIndices;
        private readonly ulong[] _candidateStamps;
        private readonly BlackboardObserverCallback _dirtyObserver;
        private readonly int _ownerThreadId;
        private int _trackedCount;
        private readonly int _maxKeys;
        private RuntimeBlackboard _attachedBlackboard;
        private int _acceptDirtySignals;
        private int _dirtySignal;
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
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _trackedKeys = new int[maxTrackedKeys];
            _lastStamps = new ulong[maxTrackedKeys];
            _trackedIndexByKey = new Dictionary<int, int>(maxTrackedKeys);
            _candidateMutations = new RuntimeBlackboardMutation[maxTrackedKeys];
            _candidateTrackedIndices = new int[maxTrackedKeys];
            _candidateStamps = new ulong[maxTrackedKeys];
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
            EnsureOwnerThread();
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
                MarkDirty();
            }
        }

        public void TrackKey(string key)
        {
            EnsureOwnerThread();
            EnsureNotDisposed();
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            StringHashFunction hashFunction = _attachedBlackboard != null
                ? _attachedBlackboard.StringHashFunc
                : RuntimeBlackboard.DefaultStringHashFunc;
            TrackKey(hashFunction(key));
        }

        /// <summary>
        /// Tracks a string key using the exact hash provider configured on the target blackboard.
        /// Use this overload whenever a blackboard has a per-instance hash override.
        /// </summary>
        public void TrackKey(string key, RuntimeBlackboard blackboard)
        {
            EnsureOwnerThread();
            EnsureNotDisposed();
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            TrackKey(blackboard.StringHashFunc(key));
        }

        /// <summary>
        /// Binds this delta tracker to a blackboard observer path.
        /// When attached to the same blackboard passed to TryFlush, an atomic observer signal
        /// avoids scans while no tracked key changed. A signaled flush scans the bounded key set.
        /// </summary>
        public void Attach(RuntimeBlackboard blackboard, bool flushExistingValues = true)
        {
            EnsureOwnerThread();
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
            Volatile.Write(ref _acceptDirtySignals, 1);
            int attachedCount = 0;
            try
            {
                for (; attachedCount < _trackedCount; attachedCount++)
                {
                    _attachedBlackboard.AddObserver(_trackedKeys[attachedCount], _dirtyObserver);
                }
            }
            catch
            {
                Volatile.Write(ref _acceptDirtySignals, 0);
                for (int i = 0; i < attachedCount; i++)
                {
                    _attachedBlackboard.RemoveObserver(_trackedKeys[i], _dirtyObserver);
                }

                _attachedBlackboard = null;
                ClearDirty();
                throw;
            }

            if (flushExistingValues)
            {
                MarkAllTrackedDirty();
            }
        }

        public void Detach()
        {
            EnsureOwnerThread();
            if (_disposed)
            {
                return;
            }

            Volatile.Write(ref _acceptDirtySignals, 0);
            RuntimeBlackboard blackboard = _attachedBlackboard;
            if (blackboard != null && !blackboard.IsDisposed)
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
            return TryFlush(bb, int.MaxValue, out patchSegment);
        }

        /// <summary>
        /// Builds an exact bounded patch in two phases. When the candidate patch exceeds
        /// maxPatchBytes, no stamps or dirty flags are consumed so the caller can retry.
        /// </summary>
        public bool TryFlush(
            RuntimeBlackboard bb,
            int maxPatchBytes,
            out ArraySegment<byte> patchSegment)
        {
            EnsureOwnerThread();
            EnsureNotDisposed();

            if (bb == null)
            {
                throw new ArgumentNullException(nameof(bb));
            }

            if (maxPatchBytes < PATCH_HEADER_SIZE)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPatchBytes));
            }

            if (_attachedBlackboard == bb)
            {
                return TryFlushDirty(bb, maxPatchBytes, out patchSegment);
            }

            return TryFlushTrackedScan(bb, maxPatchBytes, out patchSegment);
        }

        public void Dispose()
        {
            EnsureOwnerThread();
            if (_disposed)
            {
                return;
            }

            Detach();
            _disposed = true;
            _flushWriter.Dispose();
            _flushStream.Dispose();
        }

        private bool TryFlushDirty(
            RuntimeBlackboard bb,
            int maxPatchBytes,
            out ArraySegment<byte> patchSegment)
        {
            if (Interlocked.Exchange(ref _dirtySignal, 0) == 0)
            {
                patchSegment = default;
                return false;
            }

            try
            {
                int candidateCount = 0;
                int candidateBytes = PATCH_HEADER_SIZE;
                for (int i = 0; i < _trackedCount; i++)
                {
                    CaptureTrackedKeyIfChanged(bb, i, ref candidateCount, ref candidateBytes);
                }

                ValidateCandidateBudget(candidateBytes, maxPatchBytes);
                return WriteCandidates(candidateCount, out patchSegment);
            }
            catch
            {
                Interlocked.Exchange(ref _dirtySignal, 1);
                throw;
            }
        }

        private bool TryFlushTrackedScan(
            RuntimeBlackboard bb,
            int maxPatchBytes,
            out ArraySegment<byte> patchSegment)
        {
            int candidateCount = 0;
            int candidateBytes = PATCH_HEADER_SIZE;
            for (int i = 0; i < _trackedCount; i++)
            {
                CaptureTrackedKeyIfChanged(bb, i, ref candidateCount, ref candidateBytes);
            }

            ValidateCandidateBudget(candidateBytes, maxPatchBytes);
            return WriteCandidates(candidateCount, out patchSegment);
        }

        private void BeginPatch(out long bodyLengthPosition, out long countPosition)
        {
            _flushStream.Position = 0;
            _flushStream.SetLength(0);
            _flushWriter.Write(PATCH_MAGIC);
            _flushWriter.Write(PATCH_VERSION);
            _flushWriter.Write(PATCH_HEADER_SIZE);
            bodyLengthPosition = _flushStream.Position;
            _flushWriter.Write(0);
            countPosition = _flushStream.Position;
            _flushWriter.Write(0);
        }

        private bool CompletePatch(
            long bodyLengthPosition,
            long countPosition,
            int changedCount,
            out ArraySegment<byte> patchSegment)
        {
            if (changedCount == 0)
            {
                patchSegment = default;
                return false;
            }

            _flushWriter.Flush();
            int encodedLength = checked((int)_flushStream.Length);
            int bodyLength = checked(encodedLength - PATCH_HEADER_SIZE);
            _flushStream.Position = bodyLengthPosition;
            _flushWriter.Write(bodyLength);
            _flushStream.Position = countPosition;
            _flushWriter.Write(changedCount);
            _flushWriter.Flush();

            patchSegment = new ArraySegment<byte>(_flushStream.GetBuffer(), 0, encodedLength);
            return true;
        }

        private void CaptureTrackedKeyIfChanged(
            RuntimeBlackboard bb,
            int trackedIndex,
            ref int candidateCount,
            ref int candidateBytes)
        {
            int key = _trackedKeys[trackedIndex];
            if (!bb.TryCaptureNetworkMutation(key, out RuntimeBlackboardMutation mutation, out ulong stamp))
            {
                throw new InvalidOperationException(
                    $"Blackboard delta key {key} contains an object value, which cannot cross the network boundary.");
            }

            if (stamp == _lastStamps[trackedIndex])
            {
                return;
            }

            _candidateMutations[candidateCount] = mutation;
            _candidateTrackedIndices[candidateCount] = trackedIndex;
            _candidateStamps[candidateCount] = stamp;
            candidateBytes = checked(candidateBytes + GetEncodedMutationSize(mutation.Kind));
            candidateCount++;
        }

        private bool WriteCandidates(int candidateCount, out ArraySegment<byte> patchSegment)
        {
            if (candidateCount == 0)
            {
                patchSegment = default;
                return false;
            }

            BeginPatch(out long bodyLengthPosition, out long countPosition);
            for (int i = 0; i < candidateCount; i++)
            {
                WriteMutation(_candidateMutations[i]);
            }

            bool result = CompletePatch(
                bodyLengthPosition,
                countPosition,
                candidateCount,
                out patchSegment);
            for (int i = 0; i < candidateCount; i++)
            {
                _lastStamps[_candidateTrackedIndices[i]] = _candidateStamps[i];
            }

            return result;
        }

        private void WriteMutation(RuntimeBlackboardMutation mutation)
        {
            _flushWriter.Write(mutation.Key);
            switch (mutation.Kind)
            {
                case RuntimeBlackboardMutationKind.Int:
                    _flushWriter.Write(TAG_INT);
                    _flushWriter.Write(mutation.IntValue);
                    break;
                case RuntimeBlackboardMutationKind.Float:
                    _flushWriter.Write(TAG_FLOAT);
                    _flushWriter.Write(mutation.FloatValue);
                    break;
                case RuntimeBlackboardMutationKind.Bool:
                    _flushWriter.Write(TAG_BOOL);
                    _flushWriter.Write(mutation.BoolValue ? (byte)1 : (byte)0);
                    break;
                case RuntimeBlackboardMutationKind.Vector3:
                    _flushWriter.Write(TAG_VECTOR3);
                    _flushWriter.Write(mutation.VectorValue.x);
                    _flushWriter.Write(mutation.VectorValue.y);
                    _flushWriter.Write(mutation.VectorValue.z);
                    break;
                case RuntimeBlackboardMutationKind.Long:
                    _flushWriter.Write(TAG_LONG);
                    _flushWriter.Write(mutation.LongValue);
                    break;
                case RuntimeBlackboardMutationKind.Long2:
                    _flushWriter.Write(TAG_LONG2);
                    _flushWriter.Write(mutation.Long2Value.X);
                    _flushWriter.Write(mutation.Long2Value.Y);
                    break;
                case RuntimeBlackboardMutationKind.Long3:
                    _flushWriter.Write(TAG_LONG3);
                    _flushWriter.Write(mutation.Long3Value.X);
                    _flushWriter.Write(mutation.Long3Value.Y);
                    _flushWriter.Write(mutation.Long3Value.Z);
                    break;
                case RuntimeBlackboardMutationKind.Remove:
                    _flushWriter.Write(TAG_REMOVE);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown blackboard mutation kind {mutation.Kind}.");
            }
        }

        private static int GetEncodedMutationSize(RuntimeBlackboardMutationKind kind)
        {
            const int keyAndTagBytes = sizeof(int) + sizeof(byte);
            return kind switch
            {
                RuntimeBlackboardMutationKind.Int => keyAndTagBytes + sizeof(int),
                RuntimeBlackboardMutationKind.Float => keyAndTagBytes + sizeof(float),
                RuntimeBlackboardMutationKind.Bool => keyAndTagBytes + sizeof(byte),
                RuntimeBlackboardMutationKind.Vector3 => keyAndTagBytes + (3 * sizeof(float)),
                RuntimeBlackboardMutationKind.Long => keyAndTagBytes + sizeof(long),
                RuntimeBlackboardMutationKind.Long2 => keyAndTagBytes + (2 * sizeof(long)),
                RuntimeBlackboardMutationKind.Long3 => keyAndTagBytes + (3 * sizeof(long)),
                RuntimeBlackboardMutationKind.Remove => keyAndTagBytes,
                _ => throw new InvalidOperationException($"Unknown blackboard mutation kind {kind}.")
            };
        }

        private static void ValidateCandidateBudget(int candidateBytes, int maxPatchBytes)
        {
            if (candidateBytes > maxPatchBytes)
            {
                throw new InvalidOperationException(
                    $"Blackboard delta candidate size {candidateBytes} exceeds max patch bytes {maxPatchBytes}; tracker state was retained for retry.");
            }
        }

        private void OnTrackedKeyChanged(int keyHash, RuntimeBlackboard blackboard)
        {
            if (Volatile.Read(ref _acceptDirtySignals) != 0)
            {
                Interlocked.Exchange(ref _dirtySignal, 1);
            }
        }

        private void MarkAllTrackedDirty()
        {
            if (_trackedCount > 0)
            {
                Interlocked.Exchange(ref _dirtySignal, 1);
            }
        }

        private void MarkDirty()
        {
            Interlocked.Exchange(ref _dirtySignal, 1);
        }

        private void ClearDirty()
        {
            Interlocked.Exchange(ref _dirtySignal, 0);
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"BTBlackboardDelta must be accessed from owner thread {_ownerThreadId}.");
            }
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
            Apply(bb, patch, maxPatchEntries, expectedRevision: 0UL, requireRevisionMatch: false);
        }

        /// <summary>
        /// Applies a complete delta as one revision-checked blackboard transaction.
        /// A revision mismatch aborts before mutation. Observer callbacks run after commit;
        /// callback exceptions propagate and do not roll back committed values.
        /// </summary>
        public static void Apply(
            RuntimeBlackboard bb,
            ArraySegment<byte> patch,
            int maxPatchEntries,
            ulong expectedRevision)
        {
            Apply(bb, patch, maxPatchEntries, expectedRevision, requireRevisionMatch: true);
        }

        private static void Apply(
            RuntimeBlackboard bb,
            ArraySegment<byte> patch,
            int maxPatchEntries,
            ulong expectedRevision,
            bool requireRevisionMatch)
        {
            if (patch.Array == null || bb == null || patch.Count <= 0)
            {
                return;
            }

            if (maxPatchEntries <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPatchEntries));
            }

            if (patch.Offset < 0 || patch.Count < 0 || patch.Offset > patch.Array.Length - patch.Count)
            {
                throw new InvalidDataException("Blackboard delta payload segment is outside the source buffer.");
            }

            var reader = new DeltaPayloadReader(patch.Array, patch.Offset, patch.Count);
            uint magic = reader.ReadUInt32();
            if (magic != PATCH_MAGIC)
            {
                throw new InvalidDataException("Blackboard delta payload has an invalid format marker.");
            }

            ushort version = reader.ReadUInt16();
            if (version != PATCH_VERSION)
            {
                throw new InvalidDataException($"Blackboard delta payload version {version} is not supported.");
            }

            ushort headerSize = reader.ReadUInt16();
            if (headerSize != PATCH_HEADER_SIZE)
            {
                throw new InvalidDataException($"Blackboard delta header size {headerSize} is invalid.");
            }

            int bodyLength = reader.ReadInt32();
            if (bodyLength < 0)
            {
                throw new InvalidDataException("Blackboard delta body length cannot be negative.");
            }

            int count = reader.ReadInt32();
            if (bodyLength != reader.Remaining)
            {
                throw new InvalidDataException("Blackboard delta frame length does not match its payload.");
            }
            if (count < 0)
            {
                throw new InvalidDataException("Blackboard delta entry count cannot be negative.");
            }

            if (count > maxPatchEntries)
            {
                throw new InvalidDataException(
                    $"Blackboard delta entry count {count} exceeds max patch entries {maxPatchEntries}.");
            }

            if ((long)count * MIN_ENCODED_ENTRY_SIZE > reader.Remaining)
            {
                throw new InvalidDataException(
                    "Blackboard delta entry count cannot fit in the remaining payload bytes.");
            }

            RuntimeBlackboardMutation[] entries = count > 0
                ? ArrayPool<RuntimeBlackboardMutation>.Shared.Rent(count)
                : Array.Empty<RuntimeBlackboardMutation>();

            try
            {
                for (int i = 0; i < count; i++)
                {
                    int key = reader.ReadInt32();
                    byte tag = reader.ReadByte();
                    entries[i] = new RuntimeBlackboardMutation { Key = key };

                    switch (tag)
                    {
                        case TAG_INT:
                            entries[i].Kind = RuntimeBlackboardMutationKind.Int;
                            entries[i].IntValue = reader.ReadInt32();
                            break;
                        case TAG_FLOAT:
                            entries[i].Kind = RuntimeBlackboardMutationKind.Float;
                            entries[i].FloatValue = reader.ReadSingle();
                            break;
                        case TAG_BOOL:
                            entries[i].Kind = RuntimeBlackboardMutationKind.Bool;
                            byte boolValue = reader.ReadByte();
                            if (boolValue > 1)
                            {
                                throw new InvalidDataException($"Blackboard delta bool value for key {key} must be 0 or 1.");
                            }

                            entries[i].BoolValue = boolValue != 0;
                            break;
                        case TAG_VECTOR3:
                            entries[i].Kind = RuntimeBlackboardMutationKind.Vector3;
                            entries[i].VectorValue = new UnityEngine.Vector3(
                                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            break;
                        case TAG_LONG:
                            entries[i].Kind = RuntimeBlackboardMutationKind.Long;
                            entries[i].LongValue = reader.ReadInt64();
                            break;
                        case TAG_LONG2:
                            entries[i].Kind = RuntimeBlackboardMutationKind.Long2;
                            entries[i].Long2Value = new RuntimeBlackboardLong2(reader.ReadInt64(), reader.ReadInt64());
                            break;
                        case TAG_LONG3:
                            entries[i].Kind = RuntimeBlackboardMutationKind.Long3;
                            entries[i].Long3Value = new RuntimeBlackboardLong3(
                                reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());
                            break;
                        case TAG_REMOVE:
                            entries[i].Kind = RuntimeBlackboardMutationKind.Remove;
                            break;
                        default:
                            throw new InvalidDataException($"Unknown blackboard delta value tag {tag}.");
                    }
                }

                if (!reader.IsComplete)
                {
                    throw new InvalidDataException("Blackboard delta payload contains trailing bytes.");
                }

                Array.Sort(entries, 0, count, MutationKeyComparer.Instance);
                RuntimeBlackboardSchema schema = bb.Schema;
                for (int i = 0; i < count; i++)
                {
                    if (i > 0 && entries[i - 1].Key == entries[i].Key)
                    {
                        throw new InvalidDataException($"Blackboard delta key {entries[i].Key} appears more than once.");
                    }

                    ValidateAgainst(entries[i], schema);
                }

                bb.ApplyNetworkBatch(entries, count, expectedRevision, requireRevisionMatch);
            }
            finally
            {
                if (count > 0)
                {
                    ArrayPool<RuntimeBlackboardMutation>.Shared.Return(entries, clearArray: true);
                }
            }
        }

        private static void ValidateAgainst(
            RuntimeBlackboardMutation mutation,
            RuntimeBlackboardSchema schema)
        {
            if (schema == null)
            {
                return;
            }

            if (!schema.TryGetDefinition(mutation.Key, out RuntimeBlackboardKeyDefinition definition))
            {
                throw new InvalidDataException($"Blackboard delta key {mutation.Key} is not declared in the runtime schema.");
            }

            if (!definition.UsesDelta)
            {
                throw new InvalidDataException($"Blackboard delta key {mutation.Key} is not enabled for delta synchronization.");
            }

            if (mutation.Kind == RuntimeBlackboardMutationKind.Remove)
            {
                return;
            }

            RuntimeBlackboardValueType valueType = (RuntimeBlackboardValueType)mutation.Kind;
            if (definition.ValueType != valueType)
            {
                throw new InvalidDataException(
                    $"Blackboard delta key {mutation.Key} expects {definition.ValueType}, but payload contains {valueType}.");
            }
        }

        private sealed class MutationKeyComparer : IComparer<RuntimeBlackboardMutation>
        {
            public static readonly MutationKeyComparer Instance = new MutationKeyComparer();

            private MutationKeyComparer()
            {
            }

            public int Compare(RuntimeBlackboardMutation x, RuntimeBlackboardMutation y)
            {
                return x.Key.CompareTo(y.Key);
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
            public int Remaining => _end - _position;

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

            public uint ReadUInt32()
            {
                return unchecked((uint)ReadInt32());
            }

            public ushort ReadUInt16()
            {
                EnsureAvailable(2);
                int position = _position;
                _position += 2;
                return (ushort)(_buffer[position] | (_buffer[position + 1] << 8));
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
                if (byteCount < 0 || _position < 0 || byteCount > _end - _position)
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

}
