using System;
using System.Runtime.CompilerServices;
using CycloneGames.Hash.Core;
using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    [DisallowMultipleComponent]
    public sealed class TransformKeyRegistry : MonoBehaviour
    {
        private const int LINEAR_SEARCH_THRESHOLD = 16;

        [SerializeField] private TransformKeyEntry[] Entries = Array.Empty<TransformKeyEntry>();
        [SerializeField] private bool AutoBuildOnAwake = true;
        [SerializeField] private bool IncludeNestedRegistries = true;
        [SerializeField] private bool UseTransformFindFallback;

        public int EntryCount => _runtimeEntries == null ? 0 : _runtimeEntries.Length;
        public bool IsBuilt => _isBuilt;
        public bool IsTransformFindFallbackEnabled => UseTransformFindFallback;

        private RuntimeEntry[] _runtimeEntries = Array.Empty<RuntimeEntry>();
        private TransformKeyRegistry[] _nestedRegistries = Array.Empty<TransformKeyRegistry>();
        private Transform _cachedTransform;
        private bool _isBuilt;

        private void Awake()
        {
            if (AutoBuildOnAwake)
            {
                BuildIndex();
            }
        }

        private void OnTransformChildrenChanged()
        {
            _isBuilt = false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _isBuilt = false;
        }
#endif

        public void BuildIndex()
        {
            _cachedTransform = transform;

            int validCount = CountValidEntries();
            if (_runtimeEntries.Length != validCount)
            {
                _runtimeEntries = validCount == 0 ? Array.Empty<RuntimeEntry>() : new RuntimeEntry[validCount];
            }

            int writeIndex = 0;
            for (int i = 0; i < Entries.Length; i++)
            {
                string key = Entries[i].KeyValue;
                Transform value = Entries[i].TransformValue;
                if (string.IsNullOrEmpty(key) || value == null)
                {
                    continue;
                }

                _runtimeEntries[writeIndex] = new RuntimeEntry(ComputeStableHash(key), key, value);
                writeIndex++;
            }

            if (_runtimeEntries.Length > LINEAR_SEARCH_THRESHOLD)
            {
                Array.Sort(_runtimeEntries, RuntimeEntryComparer.Instance);
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
            return TryGetTransformInternal(key, ComputeStableHash(key), true, out value);
        }

        public bool TryGetTransform(ulong keyHash, out Transform value)
        {
            if (keyHash == 0UL)
            {
                value = null;
                return false;
            }

            EnsureBuilt();
            return TryGetTransformInternal(null, keyHash, true, out value);
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

        public bool TryGetLocalTransform(ulong keyHash, out Transform value)
        {
            if (keyHash == 0UL)
            {
                value = null;
                return false;
            }

            EnsureBuilt();
            return TryGetLocalTransformInternal(null, keyHash, out value);
        }

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

        private bool TryGetTransformInternal(string key, ulong keyHash, bool includeNested, out Transform value)
        {
            if (TryGetLocalTransformInternal(key, keyHash, out value))
            {
                return true;
            }

            if (!includeNested || !IncludeNestedRegistries)
            {
                value = null;
                return false;
            }

            for (int i = 0; i < _nestedRegistries.Length; i++)
            {
                TransformKeyRegistry registry = _nestedRegistries[i];
                if (registry != null && registry.TryGetTransformInternal(key, keyHash, true, out value))
                {
                    return true;
                }
            }

            value = null;
            return false;
        }

        private bool TryGetLocalTransformInternal(string key, ulong keyHash, out Transform value)
        {
            RuntimeEntry[] entries = _runtimeEntries;
            if (entries.Length <= LINEAR_SEARCH_THRESHOLD)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (IsMatch(entries[i], key, keyHash))
                    {
                        value = entries[i].Transform;
                        return value != null;
                    }
                }

                value = null;
                return false;
            }

            int index = BinarySearchFirstHash(entries, keyHash);
            if (index < 0)
            {
                value = null;
                return false;
            }

            for (int i = index; i < entries.Length && entries[i].Hash == keyHash; i++)
            {
                if (IsMatch(entries[i], key, keyHash))
                {
                    value = entries[i].Transform;
                    return value != null;
                }
            }

            value = null;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsMatch(RuntimeEntry entry, string key, ulong keyHash)
        {
            if (entry.Hash != keyHash)
            {
                return false;
            }

            return key == null || string.Equals(entry.Key, key, StringComparison.Ordinal);
        }

        private static int BinarySearchFirstHash(RuntimeEntry[] entries, ulong keyHash)
        {
            int left = 0;
            int right = entries.Length - 1;
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

        private int CountValidEntries()
        {
            int count = 0;
            for (int i = 0; i < Entries.Length; i++)
            {
                if (!string.IsNullOrEmpty(Entries[i].KeyValue) && Entries[i].TransformValue != null)
                {
                    count++;
                }
            }

            return count;
        }

        private void CacheNestedRegistries()
        {
            if (!IncludeNestedRegistries || _cachedTransform == null)
            {
                _nestedRegistries = Array.Empty<TransformKeyRegistry>();
                return;
            }

            int count = CountNearestChildRegistries(_cachedTransform);
            if (_nestedRegistries.Length != count)
            {
                _nestedRegistries = count == 0 ? Array.Empty<TransformKeyRegistry>() : new TransformKeyRegistry[count];
            }

            if (count > 0)
            {
                int index = 0;
                FillNearestChildRegistries(_cachedTransform, _nestedRegistries, ref index);
            }
        }

        private static int CountNearestChildRegistries(Transform root)
        {
            int count = 0;
            int childCount = root.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.TryGetComponent(out TransformKeyRegistry registry) && registry != null)
                {
                    count++;
                    continue;
                }

                count += CountNearestChildRegistries(child);
            }

            return count;
        }

        private static void FillNearestChildRegistries(Transform root, TransformKeyRegistry[] registries, ref int index)
        {
            int childCount = root.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.TryGetComponent(out TransformKeyRegistry registry) && registry != null)
                {
                    registries[index] = registry;
                    index++;
                    continue;
                }

                FillNearestChildRegistries(child, registries, ref index);
            }
        }

        [Serializable]
        public struct TransformKeyEntry
        {
            [SerializeField] private string Key;
            [SerializeField] private Transform Transform;

            public string KeyValue => Key;
            public Transform TransformValue => Transform;
        }

        private readonly struct RuntimeEntry
        {
            public readonly ulong Hash;
            public readonly string Key;
            public readonly Transform Transform;

            public RuntimeEntry(ulong hash, string key, Transform transform)
            {
                Hash = hash;
                Key = key;
                Transform = transform;
            }
        }

        private sealed class RuntimeEntryComparer : System.Collections.Generic.IComparer<RuntimeEntry>
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

                return x.Hash > y.Hash ? 1 : 0;
            }
        }
    }
}
