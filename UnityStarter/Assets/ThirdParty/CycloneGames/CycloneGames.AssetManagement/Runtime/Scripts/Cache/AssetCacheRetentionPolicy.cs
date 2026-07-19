using System;
using System.Collections.Generic;

namespace CycloneGames.AssetManagement.Runtime
{
    public enum AssetCacheEntryKind : byte
    {
        Asset = 0,
        AllAssets = 1,
        RawFile = 2,
    }

    /// <summary>
    /// Describes how multiple cache retention rules are combined.
    /// </summary>
    public enum AssetCacheRetentionRuleMode : byte
    {
        Any = 0,
        All = 1,
    }

    /// <summary>
    /// Identifies the idle cache tier that currently owns an entry.
    /// </summary>
    public enum AssetCacheIdleTier : byte
    {
        Probation = 0,
        Protected = 1,
    }

    /// <summary>
    /// Immutable metadata passed to retention rules when an idle cache entry is evaluated.
    /// </summary>
    public readonly struct AssetCacheEntryInfo
    {
        public readonly string Location;
        public readonly Type AssetType;
        public readonly AssetCacheEntryKind Kind;
        public readonly string Bucket;
        public readonly string Tag;
        public readonly string Owner;
        public readonly int AccessCount;
        public readonly long EstimatedBytes;
        public readonly TimeSpan IdleTime;
        public readonly AssetCacheIdleTier Tier;

        private readonly IReadOnlyList<string> _additionalBuckets;
        private readonly IReadOnlyList<string> _additionalTags;
        private readonly IReadOnlyList<string> _additionalOwners;

        internal AssetCacheEntryInfo(
            Cache.AssetCacheKey cacheKey,
            string bucket,
            string tag,
            string owner,
            IReadOnlyList<string> additionalBuckets,
            IReadOnlyList<string> additionalTags,
            IReadOnlyList<string> additionalOwners,
            int accessCount,
            long estimatedBytes,
            TimeSpan idleTime,
            AssetCacheIdleTier tier)
        {
            Location = cacheKey.Location;
            AssetType = cacheKey.AssetType;
            Kind = cacheKey.Kind;
            Bucket = bucket;
            Tag = tag;
            Owner = owner;
            _additionalBuckets = additionalBuckets;
            _additionalTags = additionalTags;
            _additionalOwners = additionalOwners;
            AccessCount = accessCount;
            EstimatedBytes = estimatedBytes;
            IdleTime = idleTime < TimeSpan.Zero ? TimeSpan.Zero : idleTime;
            Tier = tier;
        }

        public int BucketAssociationCount => AssociationCount(Bucket, _additionalBuckets);
        public int TagAssociationCount => AssociationCount(Tag, _additionalTags);
        public int OwnerAssociationCount => AssociationCount(Owner, _additionalOwners);

        public bool HasBucket(string bucket, bool includeChildren = false)
        {
            return MatchesBucket(Bucket, bucket, includeChildren) ||
                   MatchesAnyBucket(_additionalBuckets, bucket, includeChildren);
        }

        public bool HasTag(string tag)
        {
            return MatchesValue(Tag, _additionalTags, tag);
        }

        public bool HasOwner(string owner)
        {
            return MatchesValue(Owner, _additionalOwners, owner);
        }

        private static int AssociationCount(string primary, IReadOnlyList<string> additional)
        {
            return (string.IsNullOrEmpty(primary) ? 0 : 1) + (additional?.Count ?? 0);
        }

        private static bool MatchesAnyBucket(
            IReadOnlyList<string> values,
            string expected,
            bool includeChildren)
        {
            int count = values?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                if (MatchesBucket(values[i], expected, includeChildren)) return true;
            }

            return false;
        }

        private static bool MatchesBucket(string value, string expected, bool includeChildren)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(expected)) return false;
            return includeChildren
                ? AssetBucketPath.IsPrefixMatch(value, expected)
                : string.Equals(value, expected, StringComparison.Ordinal);
        }

        private static bool MatchesValue(
            string primary,
            IReadOnlyList<string> additional,
            string expected)
        {
            if (string.IsNullOrEmpty(expected)) return false;
            if (string.Equals(primary, expected, StringComparison.Ordinal)) return true;

            int count = additional?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(additional[i], expected, StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Evaluates whether an idle cache entry should be evicted by a retention pass.
    /// </summary>
    public interface IAssetCacheRetentionRule
    {
        bool ShouldEvict(in AssetCacheEntryInfo entry);
    }

    /// <summary>
    /// A composable retention policy for idle asset cache trimming.
    /// </summary>
    public readonly struct AssetCacheRetentionPolicy
    {
        private static readonly IReadOnlyList<IAssetCacheRetentionRule> EmptyRules = Array.Empty<IAssetCacheRetentionRule>();
        private static readonly IReadOnlyList<IAssetCacheRetentionRule> EvictAllRules =
            Array.AsReadOnly(new[] { AssetCacheRetentionRules.EvictAll });

        private readonly IReadOnlyList<IAssetCacheRetentionRule> _evictionRules;
        private readonly IReadOnlyList<IAssetCacheRetentionRule> _preserveRules;

        public readonly AssetCacheRetentionRuleMode EvictionMode;

        public IReadOnlyList<IAssetCacheRetentionRule> EvictionRules => _evictionRules ?? EmptyRules;
        public IReadOnlyList<IAssetCacheRetentionRule> PreserveRules => _preserveRules ?? EmptyRules;

        public static AssetCacheRetentionPolicy KeepAll => default;

        public static AssetCacheRetentionPolicy EvictAllIdle =>
            new AssetCacheRetentionPolicy(EvictAllRules);

        public AssetCacheRetentionPolicy(IAssetCacheRetentionRule evictionRule)
            : this(evictionRule == null ? null : new[] { evictionRule }, AssetCacheRetentionRuleMode.Any, null)
        {
        }

        public AssetCacheRetentionPolicy(
            IReadOnlyList<IAssetCacheRetentionRule> evictionRules,
            AssetCacheRetentionRuleMode evictionMode = AssetCacheRetentionRuleMode.Any,
            IReadOnlyList<IAssetCacheRetentionRule> preserveRules = null)
        {
            if (evictionMode != AssetCacheRetentionRuleMode.Any &&
                evictionMode != AssetCacheRetentionRuleMode.All)
            {
                throw new ArgumentOutOfRangeException(nameof(evictionMode));
            }

            _evictionRules = CopyRules(evictionRules, nameof(evictionRules));
            _preserveRules = CopyRules(preserveRules, nameof(preserveRules));
            EvictionMode = evictionMode;
        }

        public static AssetCacheRetentionPolicy IdleForAtLeast(TimeSpan minimumIdleTime)
        {
            if (minimumIdleTime <= TimeSpan.Zero)
            {
                return EvictAllIdle;
            }

            return new AssetCacheRetentionPolicy(AssetCacheRetentionRules.IdleForAtLeast(minimumIdleTime));
        }

        public static AssetCacheRetentionPolicy MatchingAny(params IAssetCacheRetentionRule[] evictionRules)
        {
            return new AssetCacheRetentionPolicy(evictionRules, AssetCacheRetentionRuleMode.Any, null);
        }

        public static AssetCacheRetentionPolicy MatchingAll(params IAssetCacheRetentionRule[] evictionRules)
        {
            return new AssetCacheRetentionPolicy(evictionRules, AssetCacheRetentionRuleMode.All, null);
        }

        public AssetCacheRetentionPolicy WithPreserveRules(params IAssetCacheRetentionRule[] preserveRules)
        {
            return new AssetCacheRetentionPolicy(_evictionRules, EvictionMode, preserveRules);
        }

        public bool ShouldEvict(in AssetCacheEntryInfo entry)
        {
            if (MatchesAny(_preserveRules, in entry))
            {
                return false;
            }

            return EvictionMode == AssetCacheRetentionRuleMode.All
                ? MatchesAll(_evictionRules, in entry)
                : MatchesAny(_evictionRules, in entry);
        }

        private static bool MatchesAny(IReadOnlyList<IAssetCacheRetentionRule> rules, in AssetCacheEntryInfo entry)
        {
            int count = rules?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var rule = rules[i];
                if (rule != null && rule.ShouldEvict(in entry))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAll(IReadOnlyList<IAssetCacheRetentionRule> rules, in AssetCacheEntryInfo entry)
        {
            int count = rules?.Count ?? 0;
            bool hasRule = false;

            for (int i = 0; i < count; i++)
            {
                var rule = rules[i];
                if (rule == null)
                {
                    continue;
                }

                hasRule = true;
                if (!rule.ShouldEvict(in entry))
                {
                    return false;
                }
            }

            return hasRule;
        }

        private static IReadOnlyList<IAssetCacheRetentionRule> CopyRules(
            IReadOnlyList<IAssetCacheRetentionRule> rules,
            string parameterName)
        {
            int count = rules?.Count ?? 0;
            if (count == 0)
            {
                return EmptyRules;
            }

            var copy = new IAssetCacheRetentionRule[count];
            for (int i = 0; i < count; i++)
            {
                copy[i] = rules[i] ?? throw new ArgumentException(
                    "Cache retention rule collections cannot contain null entries.",
                    parameterName);
            }

            return Array.AsReadOnly(copy);
        }
    }

    /// <summary>
    /// Factory methods for common cache retention rules.
    /// </summary>
    public static class AssetCacheRetentionRules
    {
        public static readonly IAssetCacheRetentionRule EvictAll = new EvictAllRule();

        public static IAssetCacheRetentionRule IdleForAtLeast(TimeSpan minimumIdleTime)
        {
            return minimumIdleTime <= TimeSpan.Zero ? EvictAll : new MinimumIdleTimeRule(minimumIdleTime);
        }

        public static IAssetCacheRetentionRule Bucket(string bucket, bool includeChildren = false)
        {
            return new BucketRule(bucket, includeChildren);
        }

        public static IAssetCacheRetentionRule Tag(string tag)
        {
            return new TagRule(tag);
        }

        public static IAssetCacheRetentionRule Owner(string owner)
        {
            return new OwnerRule(owner);
        }

        public static IAssetCacheRetentionRule Any(params IAssetCacheRetentionRule[] rules)
        {
            return new CompositeRule(rules, AssetCacheRetentionRuleMode.Any);
        }

        public static IAssetCacheRetentionRule All(params IAssetCacheRetentionRule[] rules)
        {
            return new CompositeRule(rules, AssetCacheRetentionRuleMode.All);
        }

        private sealed class EvictAllRule : IAssetCacheRetentionRule
        {
            public bool ShouldEvict(in AssetCacheEntryInfo entry)
            {
                return true;
            }
        }

        private sealed class MinimumIdleTimeRule : IAssetCacheRetentionRule
        {
            private readonly TimeSpan _minimumIdleTime;

            public MinimumIdleTimeRule(TimeSpan minimumIdleTime)
            {
                _minimumIdleTime = minimumIdleTime;
            }

            public bool ShouldEvict(in AssetCacheEntryInfo entry)
            {
                return entry.IdleTime >= _minimumIdleTime;
            }
        }

        private sealed class BucketRule : IAssetCacheRetentionRule
        {
            private readonly string _bucket;
            private readonly bool _includeChildren;

            public BucketRule(string bucket, bool includeChildren)
            {
                _bucket = bucket;
                _includeChildren = includeChildren;
            }

            public bool ShouldEvict(in AssetCacheEntryInfo entry)
            {
                return entry.HasBucket(_bucket, _includeChildren);
            }
        }

        private sealed class TagRule : IAssetCacheRetentionRule
        {
            private readonly string _tag;

            public TagRule(string tag)
            {
                _tag = tag;
            }

            public bool ShouldEvict(in AssetCacheEntryInfo entry)
            {
                return entry.HasTag(_tag);
            }
        }

        private sealed class OwnerRule : IAssetCacheRetentionRule
        {
            private readonly string _owner;

            public OwnerRule(string owner)
            {
                _owner = owner;
            }

            public bool ShouldEvict(in AssetCacheEntryInfo entry)
            {
                return entry.HasOwner(_owner);
            }
        }

        private sealed class CompositeRule : IAssetCacheRetentionRule
        {
            private readonly IReadOnlyList<IAssetCacheRetentionRule> _rules;
            private readonly AssetCacheRetentionRuleMode _mode;

            public CompositeRule(IReadOnlyList<IAssetCacheRetentionRule> rules, AssetCacheRetentionRuleMode mode)
            {
                if (mode != AssetCacheRetentionRuleMode.Any &&
                    mode != AssetCacheRetentionRuleMode.All)
                {
                    throw new ArgumentOutOfRangeException(nameof(mode));
                }

                int count = rules?.Count ?? 0;
                if (count == 0)
                {
                    _rules = Array.Empty<IAssetCacheRetentionRule>();
                }
                else
                {
                    var copy = new IAssetCacheRetentionRule[count];
                    for (int i = 0; i < count; i++)
                    {
                        copy[i] = rules[i] ?? throw new ArgumentException(
                            "Composite cache retention rules cannot contain null entries.",
                            nameof(rules));
                    }

                    _rules = Array.AsReadOnly(copy);
                }

                _mode = mode;
            }

            public bool ShouldEvict(in AssetCacheEntryInfo entry)
            {
                int count = _rules?.Count ?? 0;
                bool hasRule = false;

                for (int i = 0; i < count; i++)
                {
                    var rule = _rules[i];
                    if (rule == null)
                    {
                        continue;
                    }

                    hasRule = true;
                    bool matches = rule.ShouldEvict(in entry);
                    if (_mode == AssetCacheRetentionRuleMode.Any && matches)
                    {
                        return true;
                    }

                    if (_mode == AssetCacheRetentionRuleMode.All && !matches)
                    {
                        return false;
                    }
                }

                return _mode == AssetCacheRetentionRuleMode.All && hasRule;
            }
        }
    }
}
