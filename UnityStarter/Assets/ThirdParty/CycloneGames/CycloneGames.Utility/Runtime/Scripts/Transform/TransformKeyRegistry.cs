using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using CycloneGames.Hash.Core;

using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Builds an allocation-free lookup index over explicitly authored Transform keys.
    /// </summary>
    /// <remarks>
    /// Index construction is a cold-path operation and can allocate when authored capacity changes.
    /// String lookups verify the original key after hashing. Hash-only lookups reject detected collisions.
    /// Duplicate keys are ignored after the first valid authored entry.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TransformKeyRegistry : MonoBehaviour
    {
        private const int LinearSearchThreshold = 16;

        [SerializeField]
        private TransformKeyEntry[] Entries = Array.Empty<TransformKeyEntry>();

        [SerializeField]
        private bool AutoBuildOnAwake = true;

        [SerializeField]
        private bool IncludeNestedRegistries = true;

        [SerializeField]
        private bool UseTransformFindFallback;

        private readonly HashSet<string> _uniqueKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Transform> _hierarchyTraversal = new List<Transform>(32);
        private readonly List<TransformKeyRegistry> _nestedBuildBuffer = new List<TransformKeyRegistry>(8);
        private RuntimeEntry[] _runtimeEntries = Array.Empty<RuntimeEntry>();
        private TransformKeyRegistry[] _nestedRegistries = Array.Empty<TransformKeyRegistry>();
        private Transform _cachedTransform;
        private int _runtimeEntryCount;
        private int _duplicateKeyCount;
        private int _invalidEntryCount;
        private bool _isBuilt;

        public int EntryCount => _runtimeEntryCount;
        public int SourceEntryCount => Entries == null ? 0 : Entries.Length;
        public int DuplicateKeyCount => _duplicateKeyCount;
        public int InvalidEntryCount => _invalidEntryCount;
        public bool IsBuilt => _isBuilt;
        public bool IsTransformFindFallbackEnabled => UseTransformFindFallback;

        private void Awake()
        {
            if (AutoBuildOnAwake)
            {
                BuildIndex();
            }
        }

        private void OnEnable()
        {
            InvalidateAncestorRegistries();
        }

        private void OnDisable()
        {
            InvalidateAncestorRegistries();
        }

        private void OnTransformParentChanged()
        {
            Invalidate();
            InvalidateAncestorRegistries();
        }

        private void OnTransformChildrenChanged()
        {
            Invalidate();
            InvalidateAncestorRegistries();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Invalidate();
        }
#endif

        /// <summary>
        /// Rebuilds the local index and nested-registry cache.
        /// </summary>
        public void BuildIndex()
        {
            _cachedTransform = transform;
            TransformKeyEntry[] sourceEntries = Entries ?? Array.Empty<TransformKeyEntry>();
            int validCapacity = CountValidEntries(sourceEntries);
            EnsureRuntimeCapacity(validCapacity);

            _uniqueKeys.Clear();
            _duplicateKeyCount = 0;
            _invalidEntryCount = 0;
            int writeIndex = 0;

            for (int sourceIndex = 0; sourceIndex < sourceEntries.Length; sourceIndex++)
            {
                string key = sourceEntries[sourceIndex].KeyValue;
                Transform value = sourceEntries[sourceIndex].TransformValue;
                if (string.IsNullOrEmpty(key) || value == null)
                {
                    _invalidEntryCount++;
                    continue;
                }

                if (!_uniqueKeys.Add(key))
                {
                    _duplicateKeyCount++;
                    continue;
                }

                _runtimeEntries[writeIndex] = new RuntimeEntry(
                    ComputeStableHash(key),
                    key,
                    value,
                    sourceIndex);
                writeIndex++;
            }

            if (writeIndex < _runtimeEntryCount)
            {
                Array.Clear(_runtimeEntries, writeIndex, _runtimeEntryCount - writeIndex);
            }

            _runtimeEntryCount = writeIndex;
            if (_runtimeEntryCount > LinearSearchThreshold)
            {
                Array.Sort(_runtimeEntries, 0, _runtimeEntryCount, RuntimeEntryComparer.Instance);
            }

            CacheNestedRegistries();
            _isBuilt = true;
        }

        public void Invalidate()
        {
            _isBuilt = false;
        }

        public bool TryGetTransform(string key, out Transform value)
        {
            if (string.IsNullOrEmpty(key))
            {
                value = null;
                return false;
            }

            EnsureBuilt();
            ulong keyHash = ComputeStableHash(key);
            if (TryGetLocalTransformInternal(key, keyHash, out value))
            {
                return true;
            }

            if (!IncludeNestedRegistries)
            {
                value = null;
                return false;
            }

            for (int i = 0; i < _nestedRegistries.Length; i++)
            {
                TransformKeyRegistry registry = _nestedRegistries[i];
                if (registry == null)
                {
                    continue;
                }

                registry.EnsureBuilt();
                if (registry.TryGetLocalTransformInternal(key, keyHash, out value))
                {
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Looks up a precomputed hash and returns false if two distinct keys share that hash.
        /// Prefer the string overload at untrusted boundaries.
        /// </summary>
        public bool TryGetTransform(ulong keyHash, out Transform value)
        {
            if (keyHash == 0UL)
            {
                value = null;
                return false;
            }

            EnsureBuilt();
            string matchedKey = null;
            value = null;
            if (!TryAccumulateHashMatch(keyHash, this, ref matchedKey, ref value))
            {
                value = null;
                return false;
            }

            bool found = matchedKey != null;
            if (!IncludeNestedRegistries)
            {
                return found;
            }

            for (int i = 0; i < _nestedRegistries.Length; i++)
            {
                TransformKeyRegistry registry = _nestedRegistries[i];
                if (registry == null)
                {
                    continue;
                }

                registry.EnsureBuilt();
                if (!TryAccumulateHashMatch(keyHash, registry, ref matchedKey, ref value))
                {
                    value = null;
                    return false;
                }

                found |= matchedKey != null;
            }

            return found && value != null;
        }

        public bool TryGetLocalTransform(string key, out Transform value)
        {
            if (string.IsNullOrEmpty(key))
            {
                value = null;
                return false;
            }

            EnsureBuilt();
            return TryGetLocalTransformInternal(key, ComputeStableHash(key), out value);
        }

        /// <summary>
        /// Performs a collision-checked hash lookup in this registry only.
        /// </summary>
        public bool TryGetLocalTransform(ulong keyHash, out Transform value)
        {
            if (keyHash == 0UL)
            {
                value = null;
                return false;
            }

            EnsureBuilt();
            return TryGetLocalHashMatch(keyHash, out value, out _, out bool ambiguous) && !ambiguous;
        }

        /// <summary>
        /// Uses the authored index first and optionally falls back to <see cref="Transform.Find(string)"/>.
        /// The fallback is intended for cold compatibility paths only.
        /// </summary>
        public Transform GetTransformOrFind(string key)
        {
            if (TryGetTransform(key, out Transform value))
            {
                return value;
            }

            if (!UseTransformFindFallback || string.IsNullOrEmpty(key))
            {
                return null;
            }

            EnsureBuilt();
            return _cachedTransform == null ? null : _cachedTransform.Find(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ComputeStableHash(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return 0UL;
            }

            ulong hash = Fnv1a64.ComputeUtf16Ordinal(key);
            return hash == 0UL ? 1UL : hash;
        }

        private void EnsureBuilt()
        {
            if (!_isBuilt)
            {
                BuildIndex();
            }
        }

        private bool TryGetLocalTransformInternal(string key, ulong keyHash, out Transform value)
        {
            if (_runtimeEntryCount <= LinearSearchThreshold)
            {
                for (int i = 0; i < _runtimeEntryCount; i++)
                {
                    RuntimeEntry entry = _runtimeEntries[i];
                    if (entry.Hash == keyHash && string.Equals(entry.Key, key, StringComparison.Ordinal))
                    {
                        value = entry.Transform;
                        return value != null;
                    }
                }

                value = null;
                return false;
            }

            int index = BinarySearchFirstHash(_runtimeEntries, _runtimeEntryCount, keyHash);
            if (index < 0)
            {
                value = null;
                return false;
            }

            for (int i = index; i < _runtimeEntryCount && _runtimeEntries[i].Hash == keyHash; i++)
            {
                RuntimeEntry entry = _runtimeEntries[i];
                if (string.Equals(entry.Key, key, StringComparison.Ordinal))
                {
                    value = entry.Transform;
                    return value != null;
                }
            }

            value = null;
            return false;
        }

        private bool TryGetLocalHashMatch(
            ulong keyHash,
            out Transform value,
            out string key,
            out bool ambiguous)
        {
            int startIndex = _runtimeEntryCount <= LinearSearchThreshold
                ? 0
                : BinarySearchFirstHash(_runtimeEntries, _runtimeEntryCount, keyHash);
            if (startIndex < 0)
            {
                value = null;
                key = null;
                ambiguous = false;
                return false;
            }

            value = null;
            key = null;
            ambiguous = false;
            for (int i = startIndex; i < _runtimeEntryCount; i++)
            {
                RuntimeEntry entry = _runtimeEntries[i];
                if (entry.Hash != keyHash)
                {
                    if (_runtimeEntryCount > LinearSearchThreshold)
                    {
                        break;
                    }

                    continue;
                }

                if (key != null && !string.Equals(key, entry.Key, StringComparison.Ordinal))
                {
                    ambiguous = true;
                    value = null;
                    return false;
                }

                if (key == null)
                {
                    key = entry.Key;
                    value = entry.Transform;
                }
            }

            return key != null && value != null;
        }

        private static bool TryAccumulateHashMatch(
            ulong keyHash,
            TransformKeyRegistry registry,
            ref string matchedKey,
            ref Transform matchedValue)
        {
            if (!registry.TryGetLocalHashMatch(
                    keyHash,
                    out Transform candidateValue,
                    out string candidateKey,
                    out bool ambiguous))
            {
                return !ambiguous;
            }

            if (matchedKey != null && !string.Equals(matchedKey, candidateKey, StringComparison.Ordinal))
            {
                return false;
            }

            if (matchedKey == null)
            {
                matchedKey = candidateKey;
                matchedValue = candidateValue;
            }

            return true;
        }

        private void EnsureRuntimeCapacity(int capacity)
        {
            if (_runtimeEntries.Length == capacity)
            {
                return;
            }

            _runtimeEntries = capacity == 0 ? Array.Empty<RuntimeEntry>() : new RuntimeEntry[capacity];
            _runtimeEntryCount = 0;
        }

        private void CacheNestedRegistries()
        {
            if (!IncludeNestedRegistries || _cachedTransform == null)
            {
                _nestedRegistries = Array.Empty<TransformKeyRegistry>();
                _hierarchyTraversal.Clear();
                _nestedBuildBuffer.Clear();
                return;
            }

            _hierarchyTraversal.Clear();
            _nestedBuildBuffer.Clear();
            for (int i = _cachedTransform.childCount - 1; i >= 0; i--)
            {
                _hierarchyTraversal.Add(_cachedTransform.GetChild(i));
            }

            while (_hierarchyTraversal.Count > 0)
            {
                int lastIndex = _hierarchyTraversal.Count - 1;
                Transform current = _hierarchyTraversal[lastIndex];
                _hierarchyTraversal.RemoveAt(lastIndex);

                if (current.TryGetComponent(out TransformKeyRegistry registry) &&
                    registry != null &&
                    registry.isActiveAndEnabled)
                {
                    _nestedBuildBuffer.Add(registry);
                }

                for (int i = current.childCount - 1; i >= 0; i--)
                {
                    _hierarchyTraversal.Add(current.GetChild(i));
                }
            }

            int count = _nestedBuildBuffer.Count;
            if (_nestedRegistries.Length != count)
            {
                _nestedRegistries = count == 0
                    ? Array.Empty<TransformKeyRegistry>()
                    : new TransformKeyRegistry[count];
            }

            for (int i = 0; i < count; i++)
            {
                _nestedRegistries[i] = _nestedBuildBuffer[i];
            }

            _hierarchyTraversal.Clear();
            _nestedBuildBuffer.Clear();
        }

        private void InvalidateAncestorRegistries()
        {
            Transform current = transform.parent;
            while (current != null)
            {
                if (current.TryGetComponent(out TransformKeyRegistry registry) && registry != null)
                {
                    registry.Invalidate();
                }

                current = current.parent;
            }
        }

        private static int CountValidEntries(TransformKeyEntry[] entries)
        {
            int count = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].KeyValue) && entries[i].TransformValue != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static int BinarySearchFirstHash(RuntimeEntry[] entries, int count, ulong keyHash)
        {
            int left = 0;
            int right = count - 1;
            int found = -1;
            while (left <= right)
            {
                int middle = left + ((right - left) >> 1);
                ulong hash = entries[middle].Hash;
                if (hash < keyHash)
                {
                    left = middle + 1;
                }
                else if (hash > keyHash)
                {
                    right = middle - 1;
                }
                else
                {
                    found = middle;
                    right = middle - 1;
                }
            }

            return found;
        }

        [Serializable]
        public struct TransformKeyEntry
        {
            [SerializeField]
            private string Key;

            [SerializeField]
            private Transform Transform;

            public string KeyValue => Key;
            public Transform TransformValue => Transform;
        }

        private readonly struct RuntimeEntry
        {
            public readonly ulong Hash;
            public readonly string Key;
            public readonly Transform Transform;
            public readonly int SourceIndex;

            public RuntimeEntry(ulong hash, string key, Transform transform, int sourceIndex)
            {
                Hash = hash;
                Key = key;
                Transform = transform;
                SourceIndex = sourceIndex;
            }
        }

        private sealed class RuntimeEntryComparer : IComparer<RuntimeEntry>
        {
            public static readonly RuntimeEntryComparer Instance = new RuntimeEntryComparer();

            private RuntimeEntryComparer()
            {
            }

            public int Compare(RuntimeEntry x, RuntimeEntry y)
            {
                if (x.Hash < y.Hash)
                {
                    return -1;
                }

                if (x.Hash > y.Hash)
                {
                    return 1;
                }

                return x.SourceIndex.CompareTo(y.SourceIndex);
            }
        }
    }
}
