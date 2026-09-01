using System;
using System.Collections.Generic;

namespace CycloneGames.AtlasPipeline.Pure
{
    public enum AtlasIndexChange
    {
        None = 0,
        Added = 1,
        Moved = 2,
    }

    /// <summary>
    /// Result of adding one member to a bucket. <see cref="SpellingCanonicalized"/> is distinct from
    /// <see cref="MemberAdded"/> because it marks a project that cannot be checked out correctly on a
    /// case-insensitive filesystem — see <see cref="AtlasIndex.CaseVariantCount"/>.
    /// </summary>
    internal enum AtlasBucketChange
    {
        None = 0,
        MemberAdded = 1,
        SpellingCanonicalized = 2,
    }

    /// <summary>
    /// One generated atlas: its key, the owning rule, and the set of source assets packed into it.
    /// The ordered member list and the membership fingerprint are computed lazily and cached until
    /// the membership actually changes. This is the single largest win over the previous
    /// implementation, which rebuilt and re-sorted a fresh <see cref="List{T}"/> for every atlas on
    /// every generation pass even when nothing about that atlas had changed.
    /// </summary>
    public sealed class AtlasBucket
    {
        // Key and value are always the same string. A dictionary is used instead of a HashSet purely
        // so a stored member can be replaced: see Add, which needs to swap in a canonical spelling.
        private readonly Dictionary<string, string> _assets;
        private readonly List<string> _ordered;
        private bool _orderedValid;
        private bool _pathHashValid;
        private long _pathHash;

        internal AtlasBucket(string key)
        {
            Key = key;
            RuleId = -1;
            _assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _ordered = new List<string>();
            _orderedValid = true;
            _pathHashValid = true;
            _pathHash = AtlasHash.NullHash;
        }

        /// <summary>
        /// Atlas key. Also the file name of the generated .spriteatlasv2, so its spelling must be
        /// reproducible — see the canonicalization in <see cref="AtlasIndex.GetOrCreateBucket"/>.
        /// </summary>
        public string Key { get; internal set; }

        public int Count => _assets.Count;

        /// <summary>
        /// Index of the rule that owns this atlas. Atlas configuration (rotation, format, padding)
        /// is resolved from the owning rule, so the rule id is what lets a rule edit dirty only the
        /// atlases it governs instead of the whole project.
        /// </summary>
        public int RuleId { get; internal set; }

        /// <summary>
        /// Adds a member. <paramref name="replacedSpelling"/> receives the spelling that was dropped
        /// when the stored one had to be canonicalized, and is null in every other case.
        /// </summary>
        internal AtlasBucketChange Add(string assetPath, out string replacedSpelling)
        {
            replacedSpelling = null;

            if (_assets.TryGetValue(assetPath, out string existing))
            {
                // Already a member. Unity resolves asset paths case-insensitively on Windows and
                // macOS, so two spellings of one path are one member — but the set has to pick a
                // canonical spelling to store, otherwise whichever machine imported the path first
                // decides it. That is a real hazard: enumerate the set, sort it, and the same project
                // yields a different packable order on two machines.
                // The ordinally smallest spelling wins, which every machine can agree on.
                if (string.CompareOrdinal(assetPath, existing) >= 0)
                {
                    return AtlasBucketChange.None;
                }

                _assets.Remove(existing);
                _assets.Add(assetPath, assetPath);
                replacedSpelling = existing;
                Invalidate();
                return AtlasBucketChange.SpellingCanonicalized;
            }

            _assets.Add(assetPath, assetPath);
            Invalidate();
            return AtlasBucketChange.MemberAdded;
        }

        internal bool Remove(string assetPath)
        {
            if (!_assets.Remove(assetPath))
            {
                return false;
            }

            Invalidate();
            return true;
        }

        public bool Contains(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) && _assets.ContainsKey(assetPath);
        }

        /// <summary>
        /// Member paths in deterministic (ordinal) order. The returned list is owned by the bucket
        /// and is reused across calls; callers must not mutate it.
        /// </summary>
        public IReadOnlyList<string> GetOrdered()
        {
            EnsureOrdered();
            return _ordered;
        }

        /// <summary>
        /// Order-sensitive fingerprint of the member path list. Two buckets with the same members in
        /// the same order produce the same value on every machine.
        /// </summary>
        public long GetPathHash()
        {
            if (_pathHashValid)
            {
                return _pathHash;
            }

            EnsureOrdered();
            long hash = AtlasHash.BeginFnv1a64();
            for (int i = 0; i < _ordered.Count; i++)
            {
                AtlasHash.AppendFnv1a64(ref hash, _ordered[i]);

                // Unit separator between members. Without it, the member lists ("ab","c") and
                // ("a","bc") would hash identically and an atlas would look unchanged after a rename.
                AtlasHash.AppendFnv1a64(ref hash, '\u001F');
            }

            _pathHash = hash;
            _pathHashValid = true;
            return _pathHash;
        }

        /// <summary>
        /// Fingerprint of "membership plus the configuration that governs packing". Comparing this
        /// against the value recorded at generation time answers "does this atlas need regenerating"
        /// without loading a single sprite, which is what makes an incremental pass over tens of
        /// thousands of assets cheap.
        /// </summary>
        /// <param name="ruleFingerprint">The owning rule's packing configuration.</param>
        /// <param name="globalFingerprint">
        /// The project-wide settings that also feed packing: padding, rotation and dilation
        /// defaults, tight packing, block offset, include-in-build, output folder.
        /// Required, not optional: these change the packed result for every atlas, and a fingerprint
        /// that leaves them out lets the regeneration skip fire after a global settings edit — the
        /// change is marked dirty, then skipped anyway, and the stale packing ships silently.
        /// </param>
        public long ComputeContentHash(int ruleFingerprint, int globalFingerprint)
        {
            long hash = GetPathHash();
            AtlasHash.AppendFnv1a64(ref hash, '\u001E');

            AppendInt(ref hash, ruleFingerprint);
            AtlasHash.AppendFnv1a64(ref hash, '\u001D');
            AppendInt(ref hash, globalFingerprint);

            return hash;
        }

        private static void AppendInt(ref long hash, int value)
        {
            uint bits = (uint)value;
            for (int shift = 0; shift < 32; shift += 8)
            {
                AtlasHash.AppendFnv1a64(ref hash, (char)((bits >> shift) & 0xFFu));
            }
        }

        private void EnsureOrdered()
        {
            if (_orderedValid)
            {
                return;
            }

            _ordered.Clear();
            foreach (KeyValuePair<string, string> entry in _assets)
            {
                _ordered.Add(entry.Key);
            }

            _ordered.Sort(StringComparer.Ordinal);
            _orderedValid = true;
        }

        private void Invalidate()
        {
            _orderedValid = false;
            _pathHashValid = false;
        }
    }

    /// <summary>
    /// In-memory map between source assets and the atlases that should contain them. All Unity asset
    /// database work lives outside this type: it only ever sees path strings, which keeps it
    /// unit-testable and free of engine dependencies.
    /// </summary>
    /// <remarks>
    /// Thread affinity: not thread-safe, and deliberately so. Unity's asset database is main-thread
    /// only, so every caller of this type is already on the main thread; adding locks here would
    /// cost on every hot-path dictionary lookup and buy nothing. The only stages designed for
    /// parallelism are the pure functions in this namespace, which take no shared state.
    /// </remarks>
    public sealed class AtlasIndex
    {
        /// <summary>Cap on how many case-variant conflicts are kept for diagnostics.</summary>
        private const int MaxCaseVariantSamples = 8;

        private readonly Dictionary<string, AtlasBucket> _buckets;
        private readonly Dictionary<string, string> _assetToAtlas;
        private readonly HashSet<string> _dirtyKeys;
        private readonly List<AtlasBucket> _orderedBuckets;
        private readonly List<string> _caseVariantSamples;
        private bool _orderedBucketsValid;
        private int _caseVariantCount;

        public AtlasIndex()
        {
            _buckets = new Dictionary<string, AtlasBucket>(StringComparer.OrdinalIgnoreCase);
            _assetToAtlas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _dirtyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _orderedBuckets = new List<AtlasBucket>();
            _caseVariantSamples = new List<string>();
            _orderedBucketsValid = true;
        }

        /// <summary>
        /// How many members arrived as a second spelling of a path already in the set.
        /// Two paths differing only by case are one file on Windows and on default macOS volumes, but
        /// two files on Linux and on case-sensitive macOS volumes. A non-zero count therefore means
        /// the project is checked out differently depending on the developer's OS, and the atlases
        /// produced on those machines will not match.
        /// </summary>
        public int CaseVariantCount => _caseVariantCount;

        /// <summary>
        /// A bounded sample of the conflicting spellings, formatted as "dropped -> kept", for
        /// diagnostics. Capped so a pathological project cannot grow the list without bound.
        /// </summary>
        public IReadOnlyList<string> CaseVariantSamples => _caseVariantSamples;

        public int BucketCount => _buckets.Count;

        public int AssetCount => _assetToAtlas.Count;

        public int DirtyCount => _dirtyKeys.Count;

        public bool TryGetBucket(string atlasKey, out AtlasBucket bucket)
        {
            bucket = null;
            return !string.IsNullOrEmpty(atlasKey) && _buckets.TryGetValue(atlasKey, out bucket);
        }

        public AtlasBucket GetOrCreateBucket(string atlasKey)
        {
            if (_buckets.TryGetValue(atlasKey, out AtlasBucket bucket))
            {
                // Same canonicalization rule as the member set. The atlas key becomes the generated
                // file name, so if two spellings of one key exist the bucket has to converge on one
                // — otherwise the file name (and every runtime path built from it) would depend on
                // which machine created the bucket first.
                if (string.CompareOrdinal(atlasKey, bucket.Key) < 0)
                {
                    _buckets.Remove(bucket.Key);
                    bucket.Key = atlasKey;
                    _buckets.Add(atlasKey, bucket);
                    _orderedBucketsValid = false;
                }

                return bucket;
            }

            bucket = new AtlasBucket(atlasKey);
            _buckets.Add(atlasKey, bucket);
            _orderedBucketsValid = false;
            return bucket;
        }

        public bool TryGetAtlasKeyOf(string assetPath, out string atlasKey)
        {
            atlasKey = null;
            return !string.IsNullOrEmpty(assetPath)
                   && _assetToAtlas.TryGetValue(assetPath, out atlasKey);
        }

        public string GetAtlasKeyOf(string assetPath)
        {
            return TryGetAtlasKeyOf(assetPath, out string atlasKey) ? atlasKey : null;
        }

        public AtlasIndexChange Add(string assetPath, string atlasKey, bool markDirty)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(atlasKey))
            {
                return AtlasIndexChange.None;
            }

            bool hasPrevious = _assetToAtlas.TryGetValue(assetPath, out string previousKey);

            if (hasPrevious
                && string.Equals(previousKey, atlasKey, StringComparison.OrdinalIgnoreCase))
            {
                AtlasBucket current = GetOrCreateBucket(atlasKey);

                // Re-adding into the bucket that already owns the asset is normally a no-op, but it
                // also self-heals a bucket that lost the entry through a partial clear.
                AtlasBucketChange reAdd = current.Add(assetPath, out string replacedOnReAdd);
                if (reAdd == AtlasBucketChange.None)
                {
                    return AtlasIndexChange.None;
                }

                if (reAdd == AtlasBucketChange.SpellingCanonicalized)
                {
                    RecordCaseVariant(replacedOnReAdd, assetPath);
                }

                if (markDirty)
                {
                    _dirtyKeys.Add(atlasKey);
                }

                return AtlasIndexChange.Added;
            }

            if (hasPrevious && _buckets.TryGetValue(previousKey, out AtlasBucket previous))
            {
                previous.Remove(assetPath);
                if (markDirty)
                {
                    _dirtyKeys.Add(previousKey);
                }
            }

            AtlasBucket target = GetOrCreateBucket(atlasKey);
            if (target.Add(assetPath, out string replaced) == AtlasBucketChange.SpellingCanonicalized)
            {
                RecordCaseVariant(replaced, assetPath);
            }

            _assetToAtlas[assetPath] = atlasKey;
            if (markDirty)
            {
                _dirtyKeys.Add(atlasKey);
            }

            return hasPrevious ? AtlasIndexChange.Moved : AtlasIndexChange.Added;
        }

        public bool Remove(string assetPath, bool markDirty, out string affectedKey)
        {
            affectedKey = null;
            if (string.IsNullOrEmpty(assetPath)
                || !_assetToAtlas.TryGetValue(assetPath, out string atlasKey))
            {
                return false;
            }

            _assetToAtlas.Remove(assetPath);
            if (_buckets.TryGetValue(atlasKey, out AtlasBucket bucket))
            {
                bucket.Remove(assetPath);
            }

            affectedKey = atlasKey;
            if (markDirty)
            {
                _dirtyKeys.Add(atlasKey);
            }

            return true;
        }

        public void MarkDirty(string atlasKey)
        {
            if (!string.IsNullOrEmpty(atlasKey) && _buckets.ContainsKey(atlasKey))
            {
                _dirtyKeys.Add(atlasKey);
            }
        }

        /// <summary>
        /// Invalidates the cached owning-rule id on every bucket. Rule ids are indices into the
        /// pipeline's rule list, so they must be dropped whenever that list is rebuilt: after a
        /// reorder the same index points at a different rule, and silently resolving the wrong rule
        /// would write the wrong packing configuration into an atlas.
        /// </summary>
        private void RecordCaseVariant(string droppedSpelling, string keptSpelling)
        {
            _caseVariantCount++;
            if (_caseVariantSamples.Count < MaxCaseVariantSamples)
            {
                _caseVariantSamples.Add(droppedSpelling + " -> " + keptSpelling);
            }
        }

        internal void ResetRuleIds()
        {
            foreach (KeyValuePair<string, AtlasBucket> entry in _buckets)
            {
                entry.Value.RuleId = -1;
            }
        }

        public void MarkAllDirty()
        {
            foreach (KeyValuePair<string, AtlasBucket> entry in _buckets)
            {
                _dirtyKeys.Add(entry.Key);
            }
        }

        public bool IsDirty(string atlasKey)
        {
            return !string.IsNullOrEmpty(atlasKey) && _dirtyKeys.Contains(atlasKey);
        }

        public void ClearDirty()
        {
            _dirtyKeys.Clear();
        }

        /// <summary>
        /// Moves the dirty keys into <paramref name="output"/> in ordinal order and clears the dirty
        /// set. Sorting here (rather than at the call site) is what keeps processing order identical
        /// across machines, which in turn keeps generated atlas content byte-identical.
        /// </summary>
        public int TakeDirtyKeys(List<string> output)
        {
            if (output == null)
            {
                return 0;
            }

            output.Clear();
            foreach (string key in _dirtyKeys)
            {
                output.Add(key);
            }

            _dirtyKeys.Clear();
            output.Sort(StringComparer.Ordinal);
            return output.Count;
        }

        /// <summary>
        /// Buckets in a stable, machine-independent order. The dictionary's own iteration order is an
        /// implementation detail and must never leak into generation order.
        /// </summary>
        public IReadOnlyList<AtlasBucket> GetBuckets()
        {
            if (!_orderedBucketsValid)
            {
                _orderedBuckets.Clear();
                foreach (KeyValuePair<string, AtlasBucket> entry in _buckets)
                {
                    _orderedBuckets.Add(entry.Value);
                }

                _orderedBuckets.Sort(
                    (left, right) => string.CompareOrdinal(left.Key, right.Key));
                _orderedBucketsValid = true;
            }

            return _orderedBuckets;
        }

        /// <summary>
        /// Drops buckets that no longer contain any asset. Their atlas files are deleted by the
        /// caller, which needs the keys to build the output paths.
        /// </summary>
        public int RemoveEmptyBuckets(ICollection<string> removedKeys)
        {
            List<string> emptyKeys = null;
            _orderedBuckets.Clear();

            foreach (KeyValuePair<string, AtlasBucket> entry in _buckets)
            {
                if (entry.Value.Count > 0)
                {
                    _orderedBuckets.Add(entry.Value);
                    continue;
                }

                if (emptyKeys == null)
                {
                    emptyKeys = new List<string>();
                }

                emptyKeys.Add(entry.Key);
            }

            _orderedBuckets.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            _orderedBucketsValid = true;

            if (emptyKeys == null)
            {
                return 0;
            }

            for (int i = 0; i < emptyKeys.Count; i++)
            {
                string key = emptyKeys[i];
                _buckets.Remove(key);
                _dirtyKeys.Remove(key);
                removedKeys?.Add(key);
            }

            return emptyKeys.Count;
        }

        public void Clear()
        {
            _buckets.Clear();
            _assetToAtlas.Clear();
            _dirtyKeys.Clear();
            _orderedBuckets.Clear();
            _caseVariantSamples.Clear();
            _caseVariantCount = 0;
            _orderedBucketsValid = true;
        }

        /// <summary>
        /// Clears membership while keeping the dirty set, used when a project-changed rescan rebuilds
        /// the index without discarding work that is still queued.
        /// </summary>
        public void ClearMembership()
        {
            _buckets.Clear();
            _assetToAtlas.Clear();
            _orderedBuckets.Clear();
            _caseVariantSamples.Clear();
            _caseVariantCount = 0;
            _orderedBucketsValid = true;
        }
    }
}
