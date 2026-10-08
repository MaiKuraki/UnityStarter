using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using CycloneGames.Hash.Core;

using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// How an authored key is qualified before it enters the index. The default stays a plain leaf name so existing
    /// assets keep resolving; the path modes exist because a flattened nested snapshot loses depth, and a set of
    /// registries that reuse leaf names ("Head", "Hand") would otherwise become ambiguous.
    /// </summary>
    public enum RegistryKeyPathMode
    {
        /// <summary>
        /// The authored key is indexed verbatim. Cheapest, and correct whenever leaf names are unique across the
        /// whole scanned subtree. Duplicate names collapse to the first authored entry, as before.
        /// </summary>
        Leaf = 0,

        /// <summary>
        /// The authored key stays authoritative, and the path relative to this registry is additionally accepted.
        /// A leaf lookup costs one extra hash per descendant that owns an ancestor prefix, so it is the right
        /// default for prop and UI hierarchies where most leaf names repeat.
        /// </summary>
        LeafThenPath = 1,

        /// <summary>
        /// Only the full path is indexed, keyed as <c>RegistryA/RegistryB/Node</c>. Unambiguous by construction,
        /// and the strictest option when a hierarchy is large enough that scanning duplicates would be wasted work.
        /// A leaf-only lookup is a guaranteed miss in this mode.
        /// </summary>
        PathOnly = 2,
    }

    /// <summary>
    /// Allocation-free key to <see cref="Transform"/> lookup index over explicitly authored entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Main thread only. <see cref="BuildIndex"/> mutates shared arrays and must not run concurrently with a
    /// lookup or with another build.
    /// </para>
    /// <para>
    /// <see cref="ComputeStableHash"/> is paired with <see cref="Fnv1a64.ComputeUtf16Ordinal(ReadOnlySpan{char})"/>:
    /// the hash folds UTF-16 code units and is independent of endianness and scripting backend, so a key hashes
    /// identically on Mono, IL2CPP, x86 and ARM. A byte-oriented hash (for example a UTF-8 fold) must not be mixed
    /// into this index.
    /// </para>
    /// <para>
    /// Every lookup overload resolves against local entries first and only then walks nested registries, so a
    /// string lookup and its precomputed-hash counterpart always agree. Hash-only lookups additionally reject a
    /// hash owned by two distinct keys (a detected collision) instead of returning a guess.
    /// </para>
    /// <para>
    /// Nested discovery flattens the whole subtree, so a registry may sit at any depth and intermediate nodes
    /// without a registry never block the walk. Because that flattening loses depth, a set of registries that
    /// reuse the same leaf names would become ambiguous; <see cref="KeyPathMode"/> restores unambiguous addressing
    /// by folding the registry chain into the key, without adding a per-hop cost.
    /// </para>
    /// <para>
    /// Lookups never build on demand: querying an unbuilt registry returns <c>false</c> and raises
    /// <see cref="IndexNotBuilt"/> once, so a missing warmup cannot inject a cold-path allocation spike into a hot
    /// loop. Build during load via <see cref="BuildIndex"/> or <see cref="BuildHierarchy"/>. <see cref="Invalidate"/>
    /// only marks the cache stale and advances <see cref="Generation"/>; it does not rebuild.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TransformKeyRegistry : MonoBehaviour
    {
        /// <summary>
        /// Sets at or below this size use a branch-light linear probe; larger sets use a binary search over the
        /// hash-sorted array, where the log2(n) probe finally beats the scan.
        /// </summary>
        private const int LinearSearchThreshold = 16;

        /// <summary>Depth at which the ancestor walk stops; a hierarchy deeper than this cannot occur in Unity.</summary>
        private const int MaxHierarchyDepth = 1024;

        private const int DefaultTraversalCapacity = 32;

        private const char HierarchyPathSeparator = '/';

        private static readonly RuntimeEntry[] EmptyEntries = Array.Empty<RuntimeEntry>();
        private static readonly TransformKeyRegistry[] EmptyRegistries = Array.Empty<TransformKeyRegistry>();
        private static readonly string HierarchyPathSeparatorString = HierarchyPathSeparator.ToString();

        /// <summary>
        /// Raised once per invalid build when a lookup reaches an unbuilt registry. Handlers run on the query path
        /// and must not allocate.
        /// </summary>
        public static event Action<TransformKeyRegistry> IndexNotBuilt;

        [SerializeField]
        private TransformKeyEntry[] Entries = Array.Empty<TransformKeyEntry>();

        [SerializeField]
        private bool AutoBuildOnAwake = true;

        [SerializeField]
        private bool IncludeNestedRegistries = true;

        [SerializeField]
        private RegistryKeyPathMode KeyPathMode = RegistryKeyPathMode.Leaf;

        [SerializeField]
        private bool UseTransformFindFallback;

        // Transient authoring scratch, released by ReleaseTransientBuffers once a build completes.
        private HashSet<string> _uniqueKeys;
        private List<Transform> _hierarchyTraversal;
        private List<TransformKeyRegistry> _nestedBuildBuffer;

        // Per-build prefix memo. Composing a path prefix costs one ancestor walk plus one string allocation,
        // so doing it per entry makes a deep chain quadratic in the entry count. Caching by Transform collapses
        // the walk to once per distinct node and lets a child reuse its parent's already-composed prefix, which
        // is what keeps a 512-deep chain linear instead of 125 ms of string building.
        private Dictionary<Transform, string> _prefixCache;

        // Reusable ancestor chain for ResolveNodePrefix, sized to the deepest walk seen so far and released with
        // the other transient buffers once a build completes.
        private Transform[] _prefixChainBuffer = Array.Empty<Transform>();

        // Depth of this registry's ancestor chain, excluding its own name. Maintained by RefreshAncestorSegments
        // so the query path never walks Unity's transform hierarchy, whose parent access is a native call.
        private int _ancestorSegmentCount;

        private RuntimeEntry[] _runtimeEntries = EmptyEntries;
        private TransformKeyRegistry[] _nestedRegistries = EmptyRegistries;
        private TransformKeyRegistry _parentRegistry;
        private Transform _cachedTransform;
        private int _runtimeEntryCount;
        private int _duplicateKeyCount;
        private int _invalidEntryCount;
        private int _unindexedEntryCount;
        private uint _generation;
        private bool _isBuilt;
        private bool _notBuiltWarned;

        public int EntryCount => _runtimeEntryCount;
        public int SourceEntryCount => Entries == null ? 0 : Entries.Length;
        public int DuplicateKeyCount => _duplicateKeyCount;
        public int InvalidEntryCount => _invalidEntryCount;

        /// <summary>
        /// Authorings that ended up addressable by no form at all: both the primary key and the qualified alias
        /// were already claimed. Non-zero means the inspector should surface a hard authoring conflict.
        /// </summary>
        public int UnindexedEntryCount => _unindexedEntryCount;
        public bool IsBuilt => _isBuilt;
        public RegistryKeyPathMode KeyPath => KeyPathMode;

        /// <summary>Segment count of this registry's authored path prefix, excluding the registry's own name.</summary>
        public int AncestorSegmentCount => _ancestorSegmentCount;

        /// <summary>Advances on every invalidation and every build.</summary>
        public uint Generation => _generation;

        /// <summary>Count of active nested registries in the flattened subtree snapshot.</summary>
        public int NestedRegistryCount => _nestedRegistries.Length;
        public bool IsTransformFindFallbackEnabled => UseTransformFindFallback;

        private void Awake()
        {
            _cachedTransform = transform;
            _parentRegistry = ResolveParentRegistry();
            RefreshAncestorSegments();
            if (AutoBuildOnAwake)
            {
                BuildIndex();
            }
        }

        private void OnEnable()
        {
            _cachedTransform = transform;
            _parentRegistry = ResolveParentRegistry();
            RefreshAncestorSegments();
            if (AutoBuildOnAwake && !_isBuilt)
            {
                BuildIndex();
            }

            InvalidateAncestorRegistries();
        }

        private void OnDisable()
        {
            // A registry that stops participating cannot guarantee its cached index or nested snapshot is still
            // valid, so it invalidates itself as well as its ancestors. The !_isBuilt guard in OnEnable then
            // rebuilds on re-enable instead of resurrecting a stale index.
            Invalidate();
            InvalidateAncestorRegistries();
        }

        private void OnDestroy()
        {
            ReleaseTransientBuffers();
        }

        private void OnTransformParentChanged()
        {
            _parentRegistry = ResolveParentRegistry();
            RefreshAncestorSegments();
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
            if (!Application.isPlaying)
            {
                _cachedTransform = transform;
                _parentRegistry = ResolveParentRegistry();
                RefreshAncestorSegments();
            }

            Invalidate();
        }
#endif

        /// <summary>
        /// Rebuilds the local index and the flattened nested-registry snapshot. The only allocating path, intended
        /// for load or explicit warmup.
        /// </summary>
        public void BuildIndex()
        {
            if (_cachedTransform == null)
            {
                _cachedTransform = transform;
            }

            TransformKeyEntry[] sourceEntries = Entries ?? Array.Empty<TransformKeyEntry>();
            bool pathAware = KeyPathMode != RegistryKeyPathMode.Leaf;
            // LeafThenPath may emit a second alias per entry, so the backing array is sized for the worst case.
            // A duplicate primary key no longer aborts its entry, so the alias can still be the only surviving
            // form of a node whose short name was already taken.
            bool mayAlias = KeyPathMode == RegistryKeyPathMode.LeafThenPath;
            int validCount = CountValidEntries(sourceEntries);
            EnsureRuntimeCapacity(mayAlias ? validCount * 2 : validCount);

            HashSet<string> uniqueKeys = _uniqueKeys ?? (_uniqueKeys = new HashSet<string>(StringComparer.Ordinal));
            uniqueKeys.Clear();
            // Only a path mode composes prefixes, so the memo is skipped entirely for the default leaf mode.
            _prefixCache = pathAware
                ? (_prefixCache ?? new Dictionary<Transform, string>(validCount))
                : null;
            _prefixCache?.Clear();
            _duplicateKeyCount = 0;
            _invalidEntryCount = 0;
            _unindexedEntryCount = 0;
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

                string effectiveKey;
                string qualifiedAlias = null;
                if (pathAware)
                {
                    // Keys stay relative to this registry only. Ancestor prefixes are applied by the lookup hop
                    // that walks down from an ancestor, so a registry is addressable both by its own short keys and
                    // through any parent's chain.
                    effectiveKey = ComposeRelativePath(_cachedTransform, value, key, out qualifiedAlias);
                }
                else
                {
                    effectiveKey = key;
                }

                // Each candidate form is admitted independently. A node whose short name is already taken must
                // still register under its qualified path, so a duplicate primary cannot discard the alias --
                // that is exactly the "LeftArm/Hand and RightArm/Hand" case, where both grandchildren share a
                // name and only the path distinguishes them.
                bool primaryWritten = false;
                if (uniqueKeys.Add(effectiveKey))
                {
                    _runtimeEntries[writeIndex++] = new RuntimeEntry(ComputeStableHash(effectiveKey), effectiveKey, value, sourceIndex);
                    primaryWritten = true;
                }
                else
                {
                    // First valid authoring wins, so the later one is reported and only its primary is dropped.
                    _duplicateKeyCount++;
                }

                if (qualifiedAlias != null && uniqueKeys.Add(qualifiedAlias))
                {
                    _runtimeEntries[writeIndex++] = new RuntimeEntry(ComputeStableHash(qualifiedAlias), qualifiedAlias, value, sourceIndex);
                }
                else if (!primaryWritten)
                {
                    // Neither form is addressable, so this authoring contributes nothing to the index.
                    _unindexedEntryCount++;
                }
            }

            // Clear past the written range over the whole retained capacity, not just the previous count:
            // leftover RuntimeEntry slots would otherwise pin Transform and string references until the next
            // reallocation. (The previous condition compared against _runtimeEntryCount, which
            // EnsureRuntimeCapacity had already zeroed, so it never ran at all.)
            if (writeIndex < _runtimeEntries.Length)
            {
                Array.Clear(_runtimeEntries, writeIndex, _runtimeEntries.Length - writeIndex);
            }

            _runtimeEntryCount = writeIndex;
            if (_runtimeEntryCount > LinearSearchThreshold)
            {
                Array.Sort(_runtimeEntries, 0, _runtimeEntryCount, RuntimeEntryComparer.Instance);
            }

            CacheNestedRegistries();

            _uniqueKeys = null;
            _prefixCache = null;
            _isBuilt = true;
            _notBuiltWarned = false;
            _generation++;
        }

        /// <summary>
        /// Resolves one authored entry into the identifier it is indexed under, relative to this registry.
        /// Returns the primary key, and optionally an additional qualified alias for the leaf-then-path mode.
        /// </summary>
        /// <remarks>
        /// The authored key is always the final segment and is never replaced by the node name, so a semantic key
        /// ("GripSocket") survives regardless of what the GameObject happens to be called. Only the prefix is
        /// derived from the hierarchy, and only in the path modes.
        /// </remarks>
        private string ComposeRelativePath(Transform root, Transform target, string key, out string qualifiedAlias)
        {
            qualifiedAlias = null;

            if (KeyPathMode == RegistryKeyPathMode.Leaf)
            {
                return key;
            }

            string prefix = ResolveRelativePrefix(root, target);
            if (prefix.Length == 0)
            {
                // A direct child has no prefix, so the bare key already is the fully qualified form.
                return key;
            }

            string qualified = string.Concat(prefix, HierarchyPathSeparator, key);
            if (KeyPathMode == RegistryKeyPathMode.PathOnly)
            {
                return qualified;
            }

            // LeafThenPath: the bare key stays directly addressable and the qualified form is added alongside it,
            // so a repeated leaf name is reachable by path while the short name keeps working.
            qualifiedAlias = qualified;
            return key;
        }

        /// <summary>
        /// Prefix for <paramref name="target"/>'s parent, memoized per node. A child reuses its own parent's
        /// already-composed prefix and appends one segment, so a chain of n distinct nodes costs n concats instead
        /// of n full ancestor walks -- the difference between linear and quadratic on a deep hierarchy.
        /// </summary>
        private string ResolveRelativePrefix(Transform root, Transform target)
        {
            if (target == null || ReferenceEquals(target, root))
            {
                return string.Empty;
            }

            Transform parent = target.parent;
            if (parent == null || ReferenceEquals(parent, root))
            {
                // A direct child of the registry root has no prefix at all.
                return string.Empty;
            }

            return ResolveNodePrefix(root, parent);
        }

        /// <summary>
        /// Path from the registry root's child level down to <paramref name="node"/>, as a slash-joined string.
        /// Empty when the node is a direct child of the root, which is what keeps a flat registry's keys unchanged.
        /// </summary>
        /// <remarks>
        /// The chain is collected by Transform reference during one upward sweep and the prefix is then composed
        /// downward, caching every step. Descending by reference (instead of re-finding each child by name) is
        /// both O(1) per hop and immune to duplicated sibling names, and a sibling or descendant of any node on
        /// the chain stops on a cache hit, so the total work over a whole subtree is one concat per distinct
        /// node rather than one full ancestor walk per node.
        /// </remarks>
        private string ResolveNodePrefix(Transform root, Transform node)
        {
            if (node == null || ReferenceEquals(node, root))
            {
                return string.Empty;
            }

            Dictionary<Transform, string> cache = _prefixCache;
            if (cache != null && cache.TryGetValue(node, out string cached))
            {
                return cached;
            }

            // Walk up to the nearest cached ancestor (or the root), counting the uncached hops. The guard bounds a
            // malformed or cyclic hierarchy instead of looping forever.
            Transform top = node;
            string basePrefix = string.Empty;
            int uncached = 0;
            while (top != null && !ReferenceEquals(top, root) && uncached < MaxHierarchyDepth)
            {
                if (cache != null && cache.TryGetValue(top, out string known))
                {
                    basePrefix = known;
                    break;
                }

                uncached++;
                top = top.parent;
            }

            if (top == null || uncached >= MaxHierarchyDepth)
            {
                // Ran off the registry subtree, or hit the depth guard: no meaningful prefix exists.
                return string.Empty;
            }

            if (uncached == 0)
            {
                // `node` is the root itself or already cached by an earlier pass.
                return basePrefix;
            }

            // Collect the uncached chain by reference, nearest node first, into the reusable buffer.
            Transform[] chain = _prefixChainBuffer;
            if (chain.Length < uncached)
            {
                chain = new Transform[Math.Max(uncached, chain.Length * 2)];
                _prefixChainBuffer = chain;
            }

            Transform cursor = node;
            for (int i = 0; i < uncached; i++)
            {
                chain[i] = cursor;
                cursor = cursor.parent;
            }

            // Compose downward from the highest uncached node, caching each prefix by reference.
            string running = basePrefix;
            for (int i = uncached - 1; i >= 0; i--)
            {
                Transform current = chain[i];
                running = running.Length == 0
                    ? current.name
                    : string.Concat(running, HierarchyPathSeparatorString, current.name);
                if (cache != null)
                {
                    cache[current] = running;
                }
            }

            return running;
        }

        /// <summary>
        /// Marks the cache stale and advances <see cref="Generation"/>. Nothing is rebuilt; the next lookup fails
        /// until an explicit <see cref="BuildIndex"/>.
        /// </summary>
        public void Invalidate()
        {
            if (!_isBuilt && _generation != 0)
            {
                return;
            }

            _isBuilt = false;
            _generation++;
        }

        public bool TryGetTransform(string key, out Transform value)
        {
            if (string.IsNullOrEmpty(key))
            {
                value = null;
                return false;
            }

            if (!EnsureBuilt())
            {
                value = null;
                return false;
            }

            ulong keyHash = ComputeStableHash(key);
            if (TryGetLocalTransformInternal(keyHash, key, out value))
            {
                return true;
            }

            if (!IncludeNestedRegistries)
            {
                value = null;
                return false;
            }

            // In the path modes a bare key may need re-qualifying against the descendant's own prefix chain, and
            // only the descendant can do that correctly (it knows its full ancestry). Delegating rather than
            // rewriting the string here keeps multi-level addressing right at any depth.
            if (KeyPathMode != RegistryKeyPathMode.Leaf)
            {
                return TryGetTransformKeyInNested(key.AsSpan(), out value);
            }

            for (int i = 0; i < _nestedRegistries.Length; i++)
            {
                TransformKeyRegistry registry = _nestedRegistries[i];
                // Null and inactive registries are filtered during the build, so the array is dense and this loop
                // never pays Unity's overloaded equality check for a destroyed object.
                if (!registry.EnsureBuilt())
                {
                    continue;
                }

                if (registry.TryGetLocalTransformInternal(keyHash, key, out value))
                {
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Path-mode nested resolution. Each descendant is offered the key both verbatim (fully qualified or
        /// self-relative, which its own local index answers) and with its own registry name optionally stripped,
        /// so a chain like <c>ParticleSystem/Trails/Emitter</c> peels one hop per level and recurses. The key is
        /// carried as a span and peeled by slicing, so a deep-chain miss allocates nothing on the query path.
        /// </summary>
        private bool TryGetTransformKeyInNested(ReadOnlySpan<char> key, out Transform value)
        {
            // Computed once per level: every verbatim retry inside the loop hashes the same characters.
            ulong keyHash = ComputeStableHash(key);
            for (int i = 0; i < _nestedRegistries.Length; i++)
            {
                TransformKeyRegistry registry = _nestedRegistries[i];
                if (!registry.EnsureBuilt())
                {
                    continue;
                }

                // Fully qualified or self-relative: the descendant's own index resolves it directly.
                if (registry.TryGetLocalTransformInternal(keyHash, key, out value))
                {
                    return true;
                }

                // Bare key under a nested registry, or a chain that starts at this descendant. An exact name
                // match yields an empty remainder, which can never match an authored entry, so it is skipped.
                if (TryStripRegistryPrefix(key, registry.name, out ReadOnlySpan<char> remainder) &&
                    remainder.Length > 0)
                {
                    if (registry.TryGetLocalTransformInternal(ComputeStableHash(remainder), remainder, out value))
                    {
                        return true;
                    }

                    if (registry.TryGetTransformKeyInNested(remainder, out value))
                    {
                        return true;
                    }
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Removes one leading <c>name/</c> segment. Returns true for an exact name match, in which case the
        /// remainder is empty. The remainder is a slice of the input, never an allocated substring.
        /// </summary>
        private static bool TryStripRegistryPrefix(ReadOnlySpan<char> key, string registryName, out ReadOnlySpan<char> remainder)
        {
            if (key.SequenceEqual(registryName.AsSpan()))
            {
                remainder = ReadOnlySpan<char>.Empty;
                return true;
            }

            if (key.Length > registryName.Length &&
                key[registryName.Length] == HierarchyPathSeparator &&
                key.StartsWith(registryName.AsSpan(), StringComparison.Ordinal))
            {
                remainder = key.Slice(registryName.Length + 1);
                return true;
            }

            remainder = default;
            return false;
        }

        /// <summary>
        /// Looks up a precomputed hash locally and then in nested registries. Returns false for a hash owned by two
        /// distinct keys; the string overload is the variant that can disambiguate such a collision.
        /// </summary>
        public bool TryGetTransform(ulong keyHash, out Transform value)
        {
            if (keyHash == 0UL)
            {
                value = null;
                return false;
            }

            if (!EnsureBuilt())
            {
                value = null;
                return false;
            }

            string matchedKey = null;
            value = null;
            if (TryGetLocalHashMatch(keyHash, out Transform localValue, out string localKey, out bool localAmbiguous))
            {
                matchedKey = localKey;
                value = localValue;
            }
            else if (localAmbiguous)
            {
                value = null;
                return false;
            }

            if (!IncludeNestedRegistries)
            {
                return matchedKey != null;
            }

            for (int i = 0; i < _nestedRegistries.Length; i++)
            {
                TransformKeyRegistry registry = _nestedRegistries[i];
                // Null and inactive registries are filtered during the build, so the array is dense and this loop
                // never pays Unity's overloaded equality check for a destroyed object.
                if (!registry.EnsureBuilt())
                {
                    continue;
                }

                if (!registry.TryGetLocalHashMatch(
                        keyHash,
                        out Transform nestedValue,
                        out string nestedKey,
                        out bool nestedAmbiguous))
                {
                    if (nestedAmbiguous)
                    {
                        value = null;
                        return false;
                    }

                    continue;
                }

                // Same hash reached two different authored keys; refuse rather than pick one.
                if (matchedKey != null && !string.Equals(matchedKey, nestedKey, StringComparison.Ordinal))
                {
                    value = null;
                    return false;
                }

                if (matchedKey == null)
                {
                    matchedKey = nestedKey;
                    value = nestedValue;
                }
            }

            return matchedKey != null && value != null;
        }

        public bool TryGetLocalTransform(string key, out Transform value)
        {
            if (string.IsNullOrEmpty(key))
            {
                value = null;
                return false;
            }

            if (!EnsureBuilt())
            {
                value = null;
                return false;
            }

            return TryGetLocalTransformInternal(ComputeStableHash(key), key, out value);
        }

        /// <summary>
        /// Collision-checked hash lookup restricted to this registry: a hash owned by two distinct keys is reported
        /// as a miss.
        /// </summary>
        public bool TryGetLocalTransform(ulong keyHash, out Transform value)
        {
            if (keyHash == 0UL)
            {
                value = null;
                return false;
            }

            if (!EnsureBuilt())
            {
                value = null;
                return false;
            }

            if (TryGetLocalHashMatch(keyHash, out value, out _, out _))
            {
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Resolves through the authored index and optionally falls back to <see cref="Transform.Find(string)"/>,
        /// which re-walks the subtree per call and is therefore for cold compatibility paths only.
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

            Transform cachedTransform = _cachedTransform != null ? _cachedTransform : transform;
            return cachedTransform == null ? null : cachedTransform.Find(key);
        }

        /// <summary>
        /// Hashes a key with the exact contract used by the index. Null or empty keys map to 0, which is a reserved
        /// sentinel and never matches an authored entry.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ComputeStableHash(string key)
        {
            return ComputeStableHash(key.AsSpan());
        }

        /// <summary>Span form of <see cref="ComputeStableHash(string)"/>, used by the allocation-free nested
        /// resolution so a peeled key never re-enters string territory.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ComputeStableHash(ReadOnlySpan<char> key)
        {
            if (key.IsEmpty)
            {
                return 0UL;
            }

            // FNV-1a can legitimately produce 0; remap it so the sentinel stays unambiguous.
            ulong hash = Fnv1a64.ComputeUtf16Ordinal(key);
            return hash == 0UL ? 1UL : hash;
        }

        /// <summary>
        /// Builds the primary identifier an entry authored at <paramref name="key"/> resolves to <em>within this
        /// registry</em>. Mirrors the exact composition performed during <see cref="BuildIndex"/>, so a caller can
        /// precompute a key for <see cref="TryGetLocalTransform(string, out Transform)"/> without hard-coding the
        /// separator or the prefix rules. Ancestor prefixes are a lookup-time concern and are not included.
        /// </summary>
        /// <param name="key">The authored leaf key or relative path.</param>
        /// <param name="target">The authored Transform, used to derive the relative path.</param>
        public string ComposeKey(string key, Transform target)
        {
            if (string.IsNullOrEmpty(key) || target == null)
            {
                return key;
            }

            Transform root = _cachedTransform != null ? _cachedTransform : transform;
            return ComposeRelativePath(root, target, key, out _);
        }

        /// <summary>The separator folding registry names and hierarchy levels into a single key.</summary>
        public static char KeyPathSeparator => HierarchyPathSeparator;

        /// <summary>
        /// Builds this registry and every nested registry below it, collapsing a whole authored hierarchy into one
        /// explicit warmup instead of per-frame first-query spikes.
        /// </summary>
        public void BuildHierarchy()
        {
            BuildIndex();
            for (int i = 0; i < _nestedRegistries.Length; i++)
            {
                // Entries are filtered to active registries at build time, so no null or inactive hop remains.
                _nestedRegistries[i].BuildHierarchy();
            }
        }

        /// <summary>Read-only build check used by lookups. Never allocates and never starts a build.</summary>
        private bool EnsureBuilt()
        {
            if (_isBuilt)
            {
                return true;
            }

            if (!_notBuiltWarned)
            {
                _notBuiltWarned = true;
                IndexNotBuilt?.Invoke(this);
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetLocalTransformInternal(ulong keyHash, string key, out Transform value)
        {
            return TryGetLocalTransformInternal(keyHash, key.AsSpan(), out value);
        }

        /// <summary>
        /// Span form of the local lookup. String comparison happens over spans so the nested path-mode resolution
        /// can query sliced keys without allocating substrings.
        /// </summary>
        private bool TryGetLocalTransformInternal(ulong keyHash, ReadOnlySpan<char> key, out Transform value)
        {
            if (_runtimeEntryCount <= LinearSearchThreshold)
            {
                for (int i = 0; i < _runtimeEntryCount; i++)
                {
                    RuntimeEntry entry = _runtimeEntries[i];
                    if (entry.Hash == keyHash && key.SequenceEqual(entry.Key.AsSpan()))
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

            // Verify the string: the hash alone cannot prove identity, so re-check every entry sharing this hash.
            for (int i = index; i < _runtimeEntryCount && _runtimeEntries[i].Hash == keyHash; i++)
            {
                RuntimeEntry entry = _runtimeEntries[i];
                if (key.SequenceEqual(entry.Key.AsSpan()))
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
            bool sorted = _runtimeEntryCount > LinearSearchThreshold;
            int startIndex = sorted ? BinarySearchFirstHash(_runtimeEntries, _runtimeEntryCount, keyHash) : 0;
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
                    // Sorted runs group equal hashes, so the first mismatch ends the run; the scatter of the
                    // unsorted small-table case must keep scanning.
                    if (sorted)
                    {
                        break;
                    }

                    continue;
                }

                if (key != null)
                {
                    if (string.Equals(key, entry.Key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // Distinct keys on one hash are only genuinely ambiguous when they disagree on the target.
                    // A leaf alias and its qualified form share a Transform by construction, so the hash overload
                    // must resolve them rather than refuse.
                    if (ReferenceEquals(value, entry.Transform))
                    {
                        continue;
                    }

                    ambiguous = true;
                    value = null;
                    return false;
                }

                key = entry.Key;
                value = entry.Transform;
            }

            return key != null && value != null;
        }

        private void EnsureRuntimeCapacity(int capacity)
        {
            // The backing array is retained across builds while it is already large enough, so a rebuild after a
            // small entry-count change does not discard and reallocate the whole store. Slots past the new count
            // are cleared by the trailing Array.Clear in BuildIndex, so nothing stale is pinned.
            if (_runtimeEntries.Length < capacity)
            {
                _runtimeEntries = new RuntimeEntry[capacity];
            }

            _runtimeEntryCount = 0;
        }

        /// <summary>
        /// Flattens every active nested registry into a depth-first, null-free array. The traversal is iterative
        /// with an explicit stack so hierarchy depth cannot overflow the call stack, and children are pushed in
        /// reverse order so the array preserves authoring order.
        /// </summary>
        /// <remarks>
        /// Filtering dead and inactive registries here rather than on the query path is what lets the lookups treat
        /// <c>_nestedRegistries</c> as dense and never pay Unity's overloaded <c>==</c> for a destroyed object.
        /// </remarks>
        private void CacheNestedRegistries()
        {
            if (!IncludeNestedRegistries || _cachedTransform == null)
            {
                _nestedRegistries = EmptyRegistries;
                ReleaseTransientBuffers();
                return;
            }

            List<Transform> traversal = _hierarchyTraversal ?? (_hierarchyTraversal = new List<Transform>(DefaultTraversalCapacity));
            List<TransformKeyRegistry> buildBuffer = _nestedBuildBuffer ?? (_nestedBuildBuffer = new List<TransformKeyRegistry>(8));
            traversal.Clear();
            buildBuffer.Clear();

            int rootChildCount = _cachedTransform.childCount;
            for (int i = rootChildCount - 1; i >= 0; i--)
            {
                traversal.Add(_cachedTransform.GetChild(i));
            }

            while (traversal.Count > 0)
            {
                int lastIndex = traversal.Count - 1;
                Transform current = traversal[lastIndex];
                traversal.RemoveAt(lastIndex);

                if (current.TryGetComponent(out TransformKeyRegistry registry) &&
                    registry != null &&
                    registry.isActiveAndEnabled)
                {
                    buildBuffer.Add(registry);
                }

                int childCount = current.childCount;
                for (int i = childCount - 1; i >= 0; i--)
                {
                    traversal.Add(current.GetChild(i));
                }
            }

            int count = buildBuffer.Count;
            TransformKeyRegistry[] nestedRegistries = _nestedRegistries;
            if (nestedRegistries.Length != count)
            {
                nestedRegistries = count == 0 ? EmptyRegistries : new TransformKeyRegistry[count];
                _nestedRegistries = nestedRegistries;
            }

            for (int i = 0; i < count; i++)
            {
                nestedRegistries[i] = buildBuffer[i];
            }

            for (int i = 0; i < count; i++)
            {
                // Seed each child with the parent hop now, so a lookup that starts deep does not have to walk
                // upward through Unity's native transform hierarchy before it can resolve a path.
                nestedRegistries[i].SetParentRegistry(this);
            }

            ReleaseTransientBuffers();
        }

        /// <summary>
        /// Records the nearest ancestor registry within the same subtree snapshot and refreshes the cached
        /// ancestor depth. Called by the parent during its build; <paramref name="parentRegistry"/> is null when
        /// this registry is not part of any snapshot.
        /// </summary>
        internal void SetParentRegistry(TransformKeyRegistry parentRegistry)
        {
            if (_parentRegistry == parentRegistry)
            {
                return;
            }

            _parentRegistry = parentRegistry;
            RefreshAncestorSegments();
        }

        /// <summary>
        /// Walks upward for the nearest active registry. Used to seed the chain when the registry becomes active,
        /// because a parent may already have cached its subtree before this child existed.
        /// </summary>
        private TransformKeyRegistry ResolveParentRegistry()
        {
            Transform current = _cachedTransform != null ? _cachedTransform.parent : null;
            int guard = 0;
            while (current != null && guard++ < MaxHierarchyDepth)
            {
                if (current.TryGetComponent(out TransformKeyRegistry registry) && registry != null && registry.isActiveAndEnabled)
                {
                    return registry;
                }

                current = current.parent;
            }

            return null;
        }

        /// <summary>
        /// Refreshes the ancestor chain depth above this one: one hop per parent registry. Runs only on
        /// lifecycle or parent changes, never on the query path.
        /// </summary>
        private void RefreshAncestorSegments()
        {
            TransformKeyRegistry parent = _parentRegistry;
            _ancestorSegmentCount = parent == null ? 0 : parent._ancestorSegmentCount + 1;
        }

        private void InvalidateAncestorRegistries()
        {
            TransformKeyRegistry current = _parentRegistry;
            int guard = 0;
            while (current != null && guard++ < MaxHierarchyDepth)
            {
                current.Invalidate();
                current = current._parentRegistry;
            }
        }

        private void ReleaseTransientBuffers()
        {
            _uniqueKeys = null;
            _hierarchyTraversal = null;
            _nestedBuildBuffer = null;
            _prefixCache = null;
            _prefixChainBuffer = Array.Empty<Transform>();
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

        /// <summary>
        /// Lower bound over equal hashes: returns the index of the first entry whose hash equals
        /// <paramref name="keyHash"/>, or -1. Narrowing <c>right</c> on a hit is what makes the result the first
        /// match rather than an arbitrary one, which keeps collision scanning deterministic.
        /// </summary>
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

                // Hashes are not unique; the authored order breaks ties so equal-hash runs stay stable.
                return x.SourceIndex.CompareTo(y.SourceIndex);
            }
        }
    }
}
