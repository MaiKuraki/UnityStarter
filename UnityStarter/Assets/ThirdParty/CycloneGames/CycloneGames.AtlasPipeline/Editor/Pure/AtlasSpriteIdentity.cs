using System;
using System.Collections.Generic;

namespace CycloneGames.AtlasPipeline.Pure
{
    /// <summary>
    /// Stable identity of one sprite inside the atlas pipeline: its source asset path plus its
    /// sprite name. Deliberately not the Unity GUID.
    /// A GUID changes when a .meta is deleted and regenerated, when a file is re-imported through
    /// some DCC round-trips, and in several merge scenarios; path and name are what the team
    /// actually agrees on. Comparing by GUID would make an atlas look stale after a harmless GUID
    /// churn, and comparing by sprite name alone would silently merge identically named sub-sprites
    /// coming from different sheets (two sheets both exporting "idle_0").
    /// </summary>
    /// <remarks>
    /// The 64-bit hash is used for fast rejection and bucketing; the original strings are kept as
    /// references (never copied) to resolve the remaining collision risk exactly. With 50k sprites
    /// a 32-bit hash would collide with roughly 29% probability, which is why the identity folds two
    /// 32-bit hashes into 64 bits (collision probability below 1e-10) and still verifies with the
    /// source strings when both sides carry them.
    /// </remarks>
    public readonly struct AtlasSpriteIdentity :
        IEquatable<AtlasSpriteIdentity>,
        IComparable<AtlasSpriteIdentity>
    {
        private readonly int _pathHash;
        private readonly int _nameHash;
        private readonly string _path;
        private readonly string _name;

        public AtlasSpriteIdentity(string assetPath, string spriteName)
        {
            _path = assetPath;
            _name = spriteName;
            _pathHash = AtlasHash.ComputePathFnv1a(assetPath);
            _nameHash = AtlasHash.ComputeFnv1a(spriteName);
        }

        private AtlasSpriteIdentity(
            string assetPath,
            string spriteName,
            int pathHash,
            int nameHash)
        {
            _path = assetPath;
            _name = spriteName;
            _pathHash = pathHash;
            _nameHash = nameHash;
        }

        /// <summary>Case-folded, separator-normalized hash of the source asset path.</summary>
        public int PathHash => _pathHash;

        /// <summary>Ordinal hash of the sprite name.</summary>
        public int NameHash => _nameHash;

        /// <summary>Source asset path, or null when the identity was built hash-only.</summary>
        public string Path => _path;

        /// <summary>Sprite name, or null when the identity was built hash-only.</summary>
        public string Name => _name;

        public bool IsValid => _pathHash != AtlasHash.NullHash;

        /// <summary>64-bit identity hash: fast to compare, allocation-free, order-sensitive.</summary>
        public long IdentityHash => CombineIdentity(_pathHash, _nameHash);

        public static AtlasSpriteIdentity FromHashes(int pathHash, int nameHash)
        {
            return new AtlasSpriteIdentity(null, null, pathHash, nameHash);
        }

        /// <summary>
        /// Total, machine-independent ordering. Sorting identity lists is how two packable sets are
        /// compared in O(n log n) without a hash set, and the order must be total (never return 0 for
        /// two different identities) or the comparison would be order-dependent and could report a
        /// stale atlas as up to date.
        /// The ordering stays consistent with <see cref="Equals"/>: path first, compared
        /// case-insensitively; then the sprite name, compared ordinally.
        /// </summary>
        public int CompareTo(AtlasSpriteIdentity other)
        {
            if (_path != null && other._path != null)
            {
                int pathComparison = string.Compare(
                    _path,
                    other._path,
                    StringComparison.OrdinalIgnoreCase);
                if (pathComparison != 0)
                {
                    return pathComparison;
                }
            }
            else if ((_path == null) != (other._path == null))
            {
                return _path == null ? -1 : 1;
            }

            if (_name != null && other._name != null)
            {
                return string.CompareOrdinal(_name, other._name);
            }

            if ((_name == null) != (other._name == null))
            {
                return _name == null ? -1 : 1;
            }

            // Both sides are hash-only (manifest comparison, where the source strings are never
            // loaded). Fall back to the hashes so the order is still total and still stable.
            int leftPathComparison = ((uint)_pathHash).CompareTo((uint)other._pathHash);
            return leftPathComparison != 0
                ? leftPathComparison
                : ((uint)_nameHash).CompareTo((uint)other._nameHash);
        }

        public bool Equals(AtlasSpriteIdentity other)
        {
            return _pathHash == other._pathHash
                   && _nameHash == other._nameHash
                   && ValueEquals(_path, other._path, ignoreCase: true)
                   && ValueEquals(_name, other._name, ignoreCase: false);
        }

        public override bool Equals(object obj)
        {
            return obj is AtlasSpriteIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            long combined = CombineIdentity(_pathHash, _nameHash);
            return (int)(combined ^ (combined >> 32));
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(_path)
                ? _name ?? string.Empty
                : _path + "/" + _name;
        }

        public static bool operator ==(AtlasSpriteIdentity left, AtlasSpriteIdentity right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AtlasSpriteIdentity left, AtlasSpriteIdentity right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Exact string comparison used to resolve hash collisions. A null on either side is treated
        /// as "unknown" rather than as a mismatch, so a hash-only identity (built during manifest
        /// comparison, where the source strings are not loaded) can still match a populated one.
        /// The path is compared case-insensitively because the case-folded path hash is too, and
        /// Windows and macOS editors disagree on casing. The sprite name is compared ordinally:
        /// "Idle_0" and "idle_0" are two different sprites in Unity and must never be merged.
        /// </summary>
        private static bool ValueEquals(string left, string right, bool ignoreCase)
        {
            if (left == null || right == null)
            {
                return true;
            }

            return ignoreCase
                ? string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
                : string.Equals(left, right, StringComparison.Ordinal);
        }

        private static long CombineIdentity(int pathHash, int nameHash)
        {
            return ((long)(uint)pathHash << 32) | (uint)nameHash;
        }

        public sealed class Comparer : IEqualityComparer<AtlasSpriteIdentity>
        {
            public static readonly Comparer Instance = new Comparer();

            public bool Equals(AtlasSpriteIdentity x, AtlasSpriteIdentity y)
            {
                return x.Equals(y);
            }

            public int GetHashCode(AtlasSpriteIdentity obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
