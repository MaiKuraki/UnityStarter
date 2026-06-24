using System;
using System.Collections.Generic;

namespace CycloneGames.AssetManagement.Runtime
{
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
        Trial = 0,
        Main = 1,
    }

    /// <summary>
    /// Immutable metadata passed to retention rules when an idle cache entry is evaluated.
    /// </summary>
    public readonly struct AssetCacheEntryInfo
    {
        public readonly string CacheKey;
        public readonly string Bucket;
        public readonly string Tag;
        public readonly string Owner;
        public readonly int AccessCount;
        public readonly long EstimatedBytes;
        public readonly TimeSpan IdleTime;
        public readonly AssetCacheIdleTier Tier;

        public AssetCacheEntryInfo(
            string cacheKey,
            string bucket,
            string tag,
            string owner,
            int accessCount,
            long estimatedBytes,
            TimeSpan idleTime,
            AssetCacheIdleTier tier)
        {
            CacheKey = cacheKey;
            Bucket = bucket;
            Tag = tag;
            Owner = owner;
            AccessCount = accessCount;
            EstimatedBytes = estimatedBytes;
            IdleTime = idleTime < TimeSpan.Zero ? TimeSpan.Zero : idleTime;
            Tier = tier;
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
            new[] { AssetCacheRetentionRules.EvictAll };

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
            _evictionRules = evictionRules;
            _preserveRules = preserveRules;
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
                if (string.IsNullOrEmpty(_bucket) || string.IsNullOrEmpty(entry.Bucket))
                {
                    return false;
                }

                return _includeChildren
                    ? AssetBucketPath.IsPrefixMatch(entry.Bucket, _bucket)
                    : string.Equals(entry.Bucket, _bucket, StringComparison.Ordinal);
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
                return !string.IsNullOrEmpty(_tag) && string.Equals(entry.Tag, _tag, StringComparison.Ordinal);
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
                return !string.IsNullOrEmpty(_owner) && string.Equals(entry.Owner, _owner, StringComparison.Ordinal);
            }
        }

        private sealed class CompositeRule : IAssetCacheRetentionRule
        {
            private readonly IReadOnlyList<IAssetCacheRetentionRule> _rules;
            private readonly AssetCacheRetentionRuleMode _mode;

            public CompositeRule(IReadOnlyList<IAssetCacheRetentionRule> rules, AssetCacheRetentionRuleMode mode)
            {
                _rules = rules;
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
