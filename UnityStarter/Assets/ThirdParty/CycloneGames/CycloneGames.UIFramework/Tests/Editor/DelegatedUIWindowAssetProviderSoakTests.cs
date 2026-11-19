using System;
using System.Threading;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.Runtime.Samples;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    /// <summary>
    /// Soak coverage for the delegated provider's lease ownership.
    /// </summary>
    [TestFixture]
    public sealed class DelegatedUIWindowAssetProviderSoakTests
    {
        [TestCase(200)]
        [TestCase(1000)]
        [TestCase(2000)]
        public void RepeatedAcquireDispose_PreservesOwnership(int iterations)
        {
            UIWindowConfiguration configuration =
                ScriptableObject.CreateInstance<UIWindowConfiguration>();
            var prefab = new GameObject("DelegatedProviderSoakPrefab");
            try
            {
                int configurationLoads = 0;
                int prefabLoads = 0;
                var provider = new DelegatedUIWindowAssetProvider(
                    (location, context, token) =>
                    {
                        configurationLoads++;
                        return UniTask.FromResult(configuration);
                    },
                    (location, context, token) =>
                    {
                        prefabLoads++;
                        return UniTask.FromResult(prefab);
                    });

                for (int i = 0; i < iterations; i++)
                {
                    IUIAssetLease<UIWindowConfiguration> configurationLease = provider
                        .AcquireConfigurationAsync("Window", default, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    Assert.AreSame(configuration, configurationLease.Asset);

                    configurationLease.Dispose();
                    configurationLease.Dispose();
                    if (i % 100 == 0)
                    {
                        Assert.Throws<ObjectDisposedException>(() =>
                        {
                            UIWindowConfiguration unused = configurationLease.Asset;
                        });
                    }

                    IUIAssetLease<GameObject> prefabLease = provider
                        .AcquirePrefabAsync(
                            new UIAssetReference("UI/Window"), default, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    Assert.AreSame(prefab, prefabLease.Asset);
                    prefabLease.Dispose();
                    prefabLease.Dispose();
                }

                Assert.AreEqual(iterations, configurationLoads);
                Assert.AreEqual(iterations, prefabLoads);
                Assert.IsNotNull(configuration);
                Assert.IsNotNull(prefab);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }
    }
}
