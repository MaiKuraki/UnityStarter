using System;
using System.Collections.Generic;
using System.Threading;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASRuntimeBackingCacheSafetyTests
    {
        private const int WorkerTimeoutMilliseconds = 5_000;

        [Test]
        public void EffectSpecBackingCache_StaleSpecAndTagViewCannotReachReplacement()
        {
            GameplayTag releasedTag = RegisterTag("Test.GAS.BackingCache.Released");
            GameplayTag replacementTag = RegisterTag("Test.GAS.BackingCache.Replacement");
            GameplayTag probeTag = RegisterTag("Test.GAS.BackingCache.Probe");
            var context = new GASRuntimeContext(
                cacheProfile: new GASRuntimeCacheProfile(effectSpecBackingCapacity: 1));
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var effect = new GameplayEffect("BackingCacheSafety", EDurationPolicy.Instant);
            GameplayEffectSpec released = GameplayEffectSpec.Create(effect, asc);
            GameplayEffectSpecTagView staleView = released.DynamicGrantedTags;

            staleView.AddTag(releasedTag);
            released.SetSetByCallerMagnitudeRaw(releasedTag, 101L);
            released.SetSetByCallerMagnitudeRaw("Released.Value", 202L);
            released.Discard();

            GASRuntimeCacheStatistics afterRelease = context.GetCacheStatistics();
            Assert.That(afterRelease.Capacity, Is.EqualTo(1));
            Assert.That(afterRelease.Retained, Is.EqualTo(1));
            Assert.That(afterRelease.Misses, Is.EqualTo(1));
            Assert.That(afterRelease.Hits, Is.Zero);

            GameplayEffectSpec replacement = GameplayEffectSpec.Create(effect, asc);
            GameplayEffectSpecTagView replacementView = replacement.DynamicGrantedTags;
            replacementView.AddTag(replacementTag);

            GASRuntimeCacheStatistics afterReplacement = context.GetCacheStatistics();
            Assert.That(afterReplacement.Retained, Is.Zero);
            Assert.That(afterReplacement.Misses, Is.EqualTo(1));
            Assert.That(afterReplacement.Hits, Is.EqualTo(1));
            Assert.That(replacement, Is.Not.SameAs(released));
            Assert.That(replacementView.ContainsRuntimeIndex(releasedTag.RuntimeIndex, explicitOnly: false), Is.False);
            Assert.That(
                replacement.GetSetByCallerMagnitudeRaw(releasedTag, warnIfNotFound: false, defaultValueRaw: -1L),
                Is.EqualTo(-1L));
            Assert.That(
                replacement.GetSetByCallerMagnitudeRaw("Released.Value", warnIfNotFound: false, defaultValueRaw: -1L),
                Is.EqualTo(-1L));

            AssertStaleTagViewRejected(staleView, probeTag);
            AssertStaleSpecReadsRejected(released, releasedTag);

            Assert.That(replacementView.ContainsRuntimeIndex(replacementTag.RuntimeIndex, explicitOnly: false), Is.True);
            Assert.That(replacementView.ContainsRuntimeIndex(releasedTag.RuntimeIndex, explicitOnly: false), Is.False);
            Assert.That(replacementView.ContainsRuntimeIndex(probeTag.RuntimeIndex, explicitOnly: false), Is.False);

            replacement.Discard();
            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void EffectSpecBackingCache_StatisticsAreBoundedAndTrimCachesRequiresOwnerThread()
        {
            var context = new GASRuntimeContext(
                threadPolicy: GASRuntimeThreadPolicy.Throw,
                cacheProfile: new GASRuntimeCacheProfile(effectSpecBackingCapacity: 2));
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var effect = new GameplayEffect("BackingCacheStatistics", EDurationPolicy.Instant);

            GASRuntimeCacheStatistics initial = context.GetCacheStatistics();
            Assert.That(initial.Capacity, Is.EqualTo(2));
            Assert.That(initial.Retained, Is.Zero);
            Assert.That(initial.Hits, Is.Zero);
            Assert.That(initial.Misses, Is.Zero);
            Assert.That(initial.Discards, Is.Zero);

            GameplayEffectSpec first = GameplayEffectSpec.Create(effect, asc);
            GameplayEffectSpec second = GameplayEffectSpec.Create(effect, asc);
            first.Discard();
            second.Discard();

            GASRuntimeCacheStatistics retained = context.GetCacheStatistics();
            Assert.That(retained.Capacity, Is.EqualTo(2));
            Assert.That(retained.Retained, Is.EqualTo(2));
            Assert.That(retained.Misses, Is.EqualTo(2));
            Assert.That(retained.Hits, Is.Zero);
            Assert.That(retained.Discards, Is.Zero);

            GameplayEffectSpec cacheHit = GameplayEffectSpec.Create(effect, asc);
            Assert.That(cacheHit, Is.Not.SameAs(first));
            Assert.That(cacheHit, Is.Not.SameAs(second));
            cacheHit.Discard();

            GASRuntimeCacheStatistics afterHit = context.GetCacheStatistics();
            Assert.That(afterHit.Retained, Is.EqualTo(2));
            Assert.That(afterHit.Misses, Is.EqualTo(2));
            Assert.That(afterHit.Hits, Is.EqualTo(1));
            Assert.That(afterHit.Discards, Is.Zero);

            Exception workerFailure = RunOnWorkerThread(context.TrimCaches);
            Assert.That(workerFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(context.GetCacheStatistics().Retained, Is.EqualTo(2));

            context.TrimCaches();

            GASRuntimeCacheStatistics trimmed = context.GetCacheStatistics();
            Assert.That(trimmed.Capacity, Is.EqualTo(2));
            Assert.That(trimmed.Retained, Is.Zero);
            Assert.That(trimmed.Misses, Is.EqualTo(2));
            Assert.That(trimmed.Hits, Is.EqualTo(1));
            Assert.That(trimmed.Discards, Is.EqualTo(2));

            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void EffectSpecBackingCache_ZeroCapacityNeverRetainsBacking()
        {
            var context = new GASRuntimeContext(
                cacheProfile: new GASRuntimeCacheProfile(effectSpecBackingCapacity: 0));
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var effect = new GameplayEffect("ZeroBackingCache", EDurationPolicy.Instant);

            GameplayEffectSpec first = GameplayEffectSpec.Create(effect, asc);
            first.Discard();
            GameplayEffectSpec second = GameplayEffectSpec.Create(effect, asc);
            second.Discard();

            GASRuntimeCacheStatistics statistics = context.GetCacheStatistics();
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(statistics.Capacity, Is.Zero);
            Assert.That(statistics.Retained, Is.Zero);
            Assert.That(statistics.Hits, Is.Zero);
            Assert.That(statistics.Misses, Is.EqualTo(2));
            Assert.That(statistics.Discards, Is.EqualTo(2));

            context.TrimCaches();
            Assert.That(context.GetCacheStatistics().Retained, Is.Zero);

            asc.Dispose();
            context.Dispose();
        }

        private static void AssertStaleTagViewRejected(
            GameplayEffectSpecTagView staleView,
            GameplayTag probeTag)
        {
            var other = new GameplayTagContainer();
            other.AddTag(probeTag);
            var parentTags = new List<GameplayTag>();
            var childTags = new List<GameplayTag>();

            Assert.Throws<ObjectDisposedException>(() => _ = staleView.IsEmpty);
            Assert.Throws<ObjectDisposedException>(() => _ = staleView.ExplicitTagCount);
            Assert.Throws<ObjectDisposedException>(() => _ = staleView.TagCount);
            Assert.Throws<ObjectDisposedException>(() => staleView.GetTags());
            Assert.Throws<ObjectDisposedException>(() => staleView.GetExplicitTags());
            Assert.Throws<ObjectDisposedException>(() => staleView.GetEnumerator());
            Assert.Throws<ObjectDisposedException>(() =>
                staleView.ContainsRuntimeIndex(probeTag.RuntimeIndex, explicitOnly: false));
            Assert.Throws<ObjectDisposedException>(() => staleView.GetParentTags(probeTag, parentTags));
            Assert.Throws<ObjectDisposedException>(() => staleView.GetChildTags(probeTag, childTags));
            Assert.Throws<ObjectDisposedException>(() => staleView.GetExplicitParentTags(probeTag, parentTags));
            Assert.Throws<ObjectDisposedException>(() => staleView.GetExplicitChildTags(probeTag, childTags));

            Assert.Throws<ObjectDisposedException>(() => staleView.AddTag(probeTag));
            Assert.Throws<ObjectDisposedException>(() => staleView.RemoveTag(probeTag));
            Assert.Throws<ObjectDisposedException>(() => staleView.AddTags(other));
            Assert.Throws<ObjectDisposedException>(() => staleView.RemoveTags(other));
            Assert.Throws<ObjectDisposedException>(staleView.Clear);
        }

        private static void AssertStaleSpecReadsRejected(
            GameplayEffectSpec released,
            GameplayTag setByCallerTag)
        {
            Assert.Throws<ObjectDisposedException>(() => _ = released.Def);
            Assert.Throws<ObjectDisposedException>(() => _ = released.Source);
            Assert.Throws<ObjectDisposedException>(() => _ = released.Target);
            Assert.Throws<ObjectDisposedException>(() => _ = released.Context);
            Assert.Throws<ObjectDisposedException>(() => _ = released.Level);
            Assert.Throws<ObjectDisposedException>(() => _ = released.Duration);
            Assert.Throws<ObjectDisposedException>(() => _ = released.DurationRaw);
            Assert.Throws<ObjectDisposedException>(() => _ = released.ModifierMagnitudes.Length);
            Assert.Throws<ObjectDisposedException>(() => _ = released.ModifierMagnitudeRawValues.Length);
            Assert.Throws<ObjectDisposedException>(() => _ = released.TargetAttributes.Length);
            Assert.Throws<ObjectDisposedException>(() => _ = released.DynamicGrantedTags);
            Assert.Throws<ObjectDisposedException>(() => _ = released.DynamicAssetTags);

            Assert.Throws<ObjectDisposedException>(() =>
                released.GetSetByCallerMagnitude(setByCallerTag, warnIfNotFound: false));
            Assert.Throws<ObjectDisposedException>(() =>
                released.GetSetByCallerMagnitudeRaw(setByCallerTag, warnIfNotFound: false));
            Assert.Throws<ObjectDisposedException>(() =>
                released.GetSetByCallerMagnitude("Released.Value", warnIfNotFound: false));
            Assert.Throws<ObjectDisposedException>(() =>
                released.GetSetByCallerMagnitudeRaw("Released.Value", warnIfNotFound: false));
            Assert.Throws<ObjectDisposedException>(() => released.HasSetByCallerMagnitude(setByCallerTag));
            Assert.Throws<ObjectDisposedException>(() => released.HasSetByCallerMagnitude("Released.Value"));
            Assert.Throws<ObjectDisposedException>(() => _ = released.SetByCallerTagMagnitudeCount);
            Assert.Throws<ObjectDisposedException>(() => _ = released.SetByCallerNameMagnitudeCount);
            Assert.Throws<ObjectDisposedException>(() => _ = released.SetByCallerMagnitudeCount);
            Assert.Throws<ObjectDisposedException>(() =>
                released.CopySetByCallerTagMagnitudes(new GameplayTag[1], new float[1]));
            Assert.Throws<ObjectDisposedException>(() =>
                released.CopySetByCallerTagMagnitudesRaw(new GameplayTag[1], new long[1]));
            Assert.Throws<ObjectDisposedException>(() =>
                released.CopySetByCallerTagStateData(new GASSetByCallerTagStateData[1]));
            Assert.Throws<ObjectDisposedException>(() =>
                released.CopySetByCallerNameMagnitudesRaw(new string[1], new long[1]));
            Assert.Throws<ObjectDisposedException>(() =>
                released.CopySetByCallerNameStateData(new GASSetByCallerNameStateData[1]));
        }

        private static GameplayTag RegisterTag(string name)
        {
            GameplayTagManager.RegisterDynamicTag(name, "GameplayAbilities backing-cache safety test tag");
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.RequestTag(name);
        }

        private static Exception RunOnWorkerThread(Action action)
        {
            Exception exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception caught)
                {
                    exception = caught;
                }
            })
            {
                IsBackground = true
            };

            thread.Start();
            if (!thread.Join(WorkerTimeoutMilliseconds))
            {
                throw new TimeoutException("The cache test worker did not finish within the timeout.");
            }

            return exception;
        }
    }
}
