// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioConfigCacheTests
    {
        [Test]
        public void HasUsableResult_UnsearchedValueRequiresDiscovery()
        {
            Assert.IsFalse(AudioConfigCache.HasUsableResult(false, null));
        }

        [Test]
        public void HasUsableResult_SearchedNullCachesTheMiss()
        {
            Assert.IsTrue(AudioConfigCache.HasUsableResult(true, null));
        }

        [Test]
        public void HasUsableResult_LiveConfigUsesTheCachedValue()
        {
            AudioPoolConfig config = ScriptableObject.CreateInstance<AudioPoolConfig>();

            try
            {
                Assert.IsTrue(AudioConfigCache.HasUsableResult(true, config));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void HasUsableResult_DestroyedConfigRequiresRediscovery()
        {
            AudioPoolConfig config = ScriptableObject.CreateInstance<AudioPoolConfig>();
            UnityEngine.Object.DestroyImmediate(config);

            Assert.IsFalse(ReferenceEquals(config, null));
            Assert.IsTrue(config == null);
            Assert.IsFalse(AudioConfigCache.HasUsableResult(true, config));
        }

        [Test]
        public void GetOrDiscover_FailureDoesNotPublishCachedMiss()
        {
            bool hasSearched = false;
            bool isSearching = false;
            AudioPoolConfig cachedConfig = null;
            int attempts = 0;
            Func<AudioPoolConfig> discover = () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("Expected discovery failure.");
                }

                return null;
            };

            Assert.Throws<InvalidOperationException>(() =>
                AudioConfigCache.GetOrDiscover(
                    ref hasSearched,
                    ref isSearching,
                    ref cachedConfig,
                    discover));
            Assert.IsFalse(hasSearched);
            Assert.IsFalse(isSearching);
            Assert.IsNull(cachedConfig);

            Assert.IsNull(AudioConfigCache.GetOrDiscover(
                ref hasSearched,
                ref isSearching,
                ref cachedConfig,
                discover));
            Assert.IsTrue(hasSearched);
            Assert.IsFalse(isSearching);
            Assert.AreEqual(2, attempts);

            Assert.IsNull(AudioConfigCache.GetOrDiscover(
                ref hasSearched,
                ref isSearching,
                ref cachedConfig,
                discover));
            Assert.AreEqual(2, attempts);
        }

        [Test]
        public void AudioPoolConfig_SetConfigBypassesAutomaticDiscovery()
        {
            AudioPoolConfig config = ScriptableObject.CreateInstance<AudioPoolConfig>();

            try
            {
                AudioPoolConfig.ClearCache();
                AudioPoolConfig.SetConfig(config);

                Assert.AreSame(config, AudioPoolConfig.FindConfig());
            }
            finally
            {
                AudioPoolConfig.ClearCache();
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void AudioPlatformProfile_SetConfigBypassesAutomaticDiscovery()
        {
            AudioPlatformProfile config = ScriptableObject.CreateInstance<AudioPlatformProfile>();

            try
            {
                AudioPlatformProfile.ClearCache();
                AudioPlatformProfile.SetConfig(config);

                Assert.AreSame(config, AudioPlatformProfile.FindConfig());
            }
            finally
            {
                AudioPlatformProfile.ClearCache();
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void AudioVoicePolicyProfile_SetConfigBypassesAutomaticDiscovery()
        {
            AudioVoicePolicyProfile config = ScriptableObject.CreateInstance<AudioVoicePolicyProfile>();

            try
            {
                AudioVoicePolicyProfile.ClearCache();
                AudioVoicePolicyProfile.SetConfig(config);

                Assert.AreSame(config, AudioVoicePolicyProfile.FindConfig());
            }
            finally
            {
                AudioVoicePolicyProfile.ClearCache();
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void AudioDuckingProfile_SetConfigBypassesAutomaticDiscovery()
        {
            AudioDuckingProfile config = ScriptableObject.CreateInstance<AudioDuckingProfile>();

            try
            {
                AudioDuckingProfile.ClearCache();
                AudioDuckingProfile.SetConfig(config);

                Assert.AreSame(config, AudioDuckingProfile.FindConfig());
            }
            finally
            {
                AudioDuckingProfile.ClearCache();
                UnityEngine.Object.DestroyImmediate(config);
            }
        }
    }
}
