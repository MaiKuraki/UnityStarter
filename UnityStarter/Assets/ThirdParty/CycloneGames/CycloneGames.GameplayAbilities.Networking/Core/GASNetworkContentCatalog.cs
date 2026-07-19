using System;
using System.Collections.Generic;
using CycloneGames.Hash.Core;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>Identifies the semantic namespace of one stable GAS content entry.</summary>
    public enum GASNetworkContentKind : byte
    {
        Invalid = 0,
        AbilityDefinition = 1,
        EffectDefinition = 2,
        Attribute = 3,
        SetByCallerName = 4,
        TargetSurface = 5
    }

    /// <summary>Immutable content identity and revision used by the GAS compatibility handshake.</summary>
    public readonly struct GASNetworkContentEntry
    {
        internal GASNetworkContentEntry(
            GASNetworkContentKind kind,
            GASNetworkContentId id,
            string stableKey,
            ulong revisionHash,
            object value)
        {
            Kind = kind;
            Id = id;
            StableKey = stableKey;
            RevisionHash = revisionHash;
            Value = value;
        }

        public GASNetworkContentKind Kind { get; }
        public GASNetworkContentId Id { get; }
        public string StableKey { get; }
        public ulong RevisionHash { get; }

        /// <summary>
        /// Optional process-local value supplied by the composition root. The wire contract never
        /// serializes this reference and does not assume that it is a Unity object.
        /// </summary>
        public object Value { get; }
    }

    /// <summary>
    /// Cold-path builder for an immutable, collision-checked GAS content catalog.
    /// </summary>
    public sealed class GASNetworkContentCatalogBuilder
    {
        public const int DefaultCapacity = 128;
        public const int MaximumEntryCount = ushort.MaxValue;
        public const int MaximumStableKeyLength = 512;

        private readonly int maximumEntryCount;
        private readonly List<GASNetworkContentEntry> entries;
        private readonly Dictionary<ContentKey, int> indexByKey;
        private readonly Dictionary<ulong, int> indexById;
        private readonly Dictionary<object, int> indexByValue;

        public GASNetworkContentCatalogBuilder(
            int capacity = DefaultCapacity,
            int maximumEntryCount = MaximumEntryCount)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (maximumEntryCount <= 0 || maximumEntryCount > MaximumEntryCount)
                throw new ArgumentOutOfRangeException(nameof(maximumEntryCount));
            if (capacity > maximumEntryCount)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            this.maximumEntryCount = maximumEntryCount;
            entries = new List<GASNetworkContentEntry>(capacity);
            indexByKey = new Dictionary<ContentKey, int>(capacity);
            indexById = new Dictionary<ulong, int>(capacity);
            indexByValue = new Dictionary<object, int>(capacity, ReferenceEqualityComparer.Instance);
        }

        public int Count => entries.Count;

        /// <summary>
        /// Registers one stable key. Revision hashes must change whenever authoritative gameplay
        /// semantics for the key change.
        /// </summary>
        public GASNetworkContentId Add(
            GASNetworkContentKind kind,
            string stableKey,
            ulong revisionHash,
            object value = null)
        {
            ValidateRegistration(kind, stableKey, revisionHash);
            if (entries.Count >= maximumEntryCount)
                throw new InvalidOperationException($"The GAS network content catalog limit of {maximumEntryCount} entries was reached.");

            var key = new ContentKey(kind, stableKey);
            if (indexByKey.ContainsKey(key))
                throw new InvalidOperationException($"GAS network content key '{kind}:{stableKey}' is already registered.");

            GASNetworkContentId id = ComputeContentId(kind, stableKey);
            if (indexById.TryGetValue(id.Value, out int collisionIndex))
            {
                GASNetworkContentEntry collision = entries[collisionIndex];
                throw new InvalidOperationException(
                    $"GAS network content ID collision between '{collision.Kind}:{collision.StableKey}' and '{kind}:{stableKey}'. Assign different stable keys.");
            }

            if (value != null && indexByValue.TryGetValue(value, out int valueIndex))
            {
                GASNetworkContentEntry previous = entries[valueIndex];
                throw new InvalidOperationException(
                    $"The same process-local value is already registered as '{previous.Kind}:{previous.StableKey}'.");
            }

            int index = entries.Count;
            entries.Add(new GASNetworkContentEntry(kind, id, stableKey, revisionHash, value));
            indexByKey.Add(key, index);
            indexById.Add(id.Value, index);
            if (value != null)
                indexByValue.Add(value, index);
            return id;
        }

        public GASNetworkContentCatalog Build()
        {
            var sorted = entries.ToArray();
            Array.Sort(sorted, GASNetworkContentEntryIdComparer.Instance);
            return new GASNetworkContentCatalog(sorted);
        }

        public static GASNetworkContentId ComputeContentId(GASNetworkContentKind kind, string stableKey)
        {
            if (!IsKnownKind(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            ValidateStableKey(stableKey);

            ulong hash = StableHash64.ComputeUtf16Ordinal(stableKey);
            hash = StableHash64.CombineUInt64LittleEndian(hash, (byte)kind);
            return new GASNetworkContentId(StableHash64.EnsureNonZero(hash));
        }

        public static ulong ComputeRevisionHash(string canonicalRevision)
        {
            if (string.IsNullOrEmpty(canonicalRevision))
                throw new ArgumentException("A canonical content revision must be non-empty.", nameof(canonicalRevision));
            return StableHash64.ComputeUtf16Ordinal(canonicalRevision);
        }

        private static void ValidateRegistration(
            GASNetworkContentKind kind,
            string stableKey,
            ulong revisionHash)
        {
            if (!IsKnownKind(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            ValidateStableKey(stableKey);
            if (revisionHash == 0UL)
                throw new ArgumentOutOfRangeException(nameof(revisionHash), "Content revisions must be non-zero.");
        }

        internal static bool IsKnownKind(GASNetworkContentKind kind)
        {
            return kind >= GASNetworkContentKind.AbilityDefinition &&
                   kind <= GASNetworkContentKind.TargetSurface;
        }

        private static void ValidateStableKey(string stableKey)
        {
            if (string.IsNullOrWhiteSpace(stableKey))
                throw new ArgumentException("A stable content key must be non-empty.", nameof(stableKey));
            if (stableKey.Length > MaximumStableKeyLength)
                throw new ArgumentException($"Stable content keys cannot exceed {MaximumStableKeyLength} characters.", nameof(stableKey));

            for (int i = 0; i < stableKey.Length; i++)
            {
                char value = stableKey[i];
                if (char.IsControl(value) || char.IsSurrogate(value))
                    throw new ArgumentException("Stable content keys cannot contain control or surrogate characters.", nameof(stableKey));
            }
        }

        private readonly struct ContentKey : IEquatable<ContentKey>
        {
            public ContentKey(GASNetworkContentKind kind, string stableKey)
            {
                Kind = kind;
                StableKey = stableKey;
            }

            private GASNetworkContentKind Kind { get; }
            private string StableKey { get; }

            public bool Equals(ContentKey other)
            {
                return Kind == other.Kind && string.Equals(StableKey, other.StableKey, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is ContentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(StableKey);
                }
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj)
            {
                return obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }

        private sealed class GASNetworkContentEntryIdComparer : IComparer<GASNetworkContentEntry>
        {
            public static readonly GASNetworkContentEntryIdComparer Instance = new GASNetworkContentEntryIdComparer();

            public int Compare(GASNetworkContentEntry x, GASNetworkContentEntry y)
            {
                return x.Id.Value.CompareTo(y.Id.Value);
            }
        }
    }

    /// <summary>
    /// Immutable, thread-safe-after-construction catalog used by codecs and runtime adapters.
    /// </summary>
    public sealed class GASNetworkContentCatalog
    {
        private readonly GASNetworkContentEntry[] entries;
        private readonly Dictionary<ulong, int> indexById;
        private readonly Dictionary<ContentKey, int> indexByKey;
        private readonly Dictionary<object, int> indexByValue;

        internal GASNetworkContentCatalog(GASNetworkContentEntry[] sortedEntries)
        {
            entries = sortedEntries ?? throw new ArgumentNullException(nameof(sortedEntries));
            indexById = new Dictionary<ulong, int>(entries.Length);
            indexByKey = new Dictionary<ContentKey, int>(entries.Length);
            indexByValue = new Dictionary<object, int>(entries.Length, ReferenceEqualityComparer.Instance);

            ulong manifestHash = Fnv1a64.OffsetBasis;
            manifestHash = StableHash64.CombineUInt64LittleEndian(manifestHash, (ulong)entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                GASNetworkContentEntry entry = entries[i];
                indexById.Add(entry.Id.Value, i);
                indexByKey.Add(new ContentKey(entry.Kind, entry.StableKey), i);
                if (entry.Value != null)
                    indexByValue.Add(entry.Value, i);

                manifestHash = StableHash64.CombineUInt64LittleEndian(manifestHash, entry.Id.Value);
                manifestHash = StableHash64.CombineUInt64LittleEndian(manifestHash, (byte)entry.Kind);
                manifestHash = StableHash64.CombineUInt64LittleEndian(manifestHash, entry.RevisionHash);
            }

            ManifestHash = StableHash64.EnsureNonZero(manifestHash);
        }

        public int Count => entries.Length;
        public ulong ManifestHash { get; }

        public GASNetworkContentEntry GetEntry(int index)
        {
            if ((uint)index >= (uint)entries.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return entries[index];
        }

        public bool TryGetEntry(GASNetworkContentId id, out GASNetworkContentEntry entry)
        {
            if (id.IsValid && indexById.TryGetValue(id.Value, out int index))
            {
                entry = entries[index];
                return true;
            }

            entry = default;
            return false;
        }

        public bool TryGetId(
            GASNetworkContentKind kind,
            string stableKey,
            out GASNetworkContentId id)
        {
            if (GASNetworkContentCatalogBuilder.IsKnownKind(kind) &&
                stableKey != null &&
                indexByKey.TryGetValue(new ContentKey(kind, stableKey), out int index))
            {
                id = entries[index].Id;
                return true;
            }

            id = default;
            return false;
        }

        public bool TryGetId(
            object value,
            GASNetworkContentKind expectedKind,
            out GASNetworkContentId id)
        {
            if (GASNetworkContentCatalogBuilder.IsKnownKind(expectedKind) &&
                value != null &&
                indexByValue.TryGetValue(value, out int index) &&
                entries[index].Kind == expectedKind)
            {
                id = entries[index].Id;
                return true;
            }

            id = default;
            return false;
        }

        public bool TryResolve(
            GASNetworkContentId id,
            GASNetworkContentKind expectedKind,
            out object value)
        {
            if (GASNetworkContentCatalogBuilder.IsKnownKind(expectedKind) &&
                id.IsValid &&
                indexById.TryGetValue(id.Value, out int index) &&
                entries[index].Kind == expectedKind &&
                entries[index].Value != null)
            {
                value = entries[index].Value;
                return true;
            }

            value = null;
            return false;
        }

        private readonly struct ContentKey : IEquatable<ContentKey>
        {
            public ContentKey(GASNetworkContentKind kind, string stableKey)
            {
                Kind = kind;
                StableKey = stableKey;
            }

            private GASNetworkContentKind Kind { get; }
            private string StableKey { get; }

            public bool Equals(ContentKey other)
            {
                return Kind == other.Kind && string.Equals(StableKey, other.StableKey, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is ContentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(StableKey);
                }
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj)
            {
                return obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
