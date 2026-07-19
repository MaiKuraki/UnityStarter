using System;
using System.Reflection;

using NUnit.Framework;

using UnityEngine.SceneManagement;

using YooAsset;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class YooAssetNativeContractTests
    {
        private const int QualifiedMaximumConcurrency = 32;

        [Test]
        public void RuntimeApi_ExposesQualifiedNativeContracts()
        {
            Assert.That(
                typeof(YooAssets).GetProperty(
                    nameof(YooAssets.IsInitialized),
                    BindingFlags.Public | BindingFlags.Static),
                Is.Not.Null);
            Assert.That(
                typeof(YooAssets).GetMethod(
                    nameof(YooAssets.SetAsyncOperationMaxTimeSlice),
                    new[] { typeof(long) }),
                Is.Not.Null);
            Assert.That(
                typeof(ResourcePackage).GetMethod(
                    nameof(ResourcePackage.InitializePackageAsync),
                    new[] { typeof(InitializePackageOptions) }),
                Is.Not.Null);
            Assert.That(
                typeof(ResourcePackage).GetMethod(
                    nameof(ResourcePackage.LoadPackageManifestAsync),
                    new[] { typeof(LoadPackageManifestOptions) }),
                Is.Not.Null);
            Assert.That(
                typeof(ResourcePackage).GetMethod(
                    nameof(ResourcePackage.ClearCacheAsync),
                    new[] { typeof(ClearCacheOptions) }),
                Is.Not.Null);
            Assert.That(
                typeof(ResourcePackage).GetMethod(
                    nameof(ResourcePackage.CreateResourceDownloader),
                    new[] { typeof(ResourceDownloaderOptions) }),
                Is.Not.Null);
            Assert.That(
                typeof(ResourcePackage).GetMethod(
                    nameof(ResourcePackage.CreateResourceDownloader),
                    new[] { typeof(BundleDownloaderOptions) }),
                Is.Not.Null);
            Assert.That(
                typeof(ResourceDownloaderOperation).GetMethod(
                    nameof(ResourceDownloaderOperation.StartDownload),
                    Type.EmptyTypes),
                Is.Not.Null);
            Assert.That(EOperationStatus.Succeeded, Is.Not.EqualTo(EOperationStatus.Failed));
        }

        [Test]
        public void LoadSceneAsync_UsesPositiveAllowActivationPolarity()
        {
            MethodInfo method = typeof(ResourcePackage).GetMethod(
                nameof(ResourcePackage.LoadSceneAsync),
                new[]
                {
                    typeof(string),
                    typeof(LoadSceneMode),
                    typeof(LocalPhysicsMode),
                    typeof(bool),
                    typeof(uint),
                });

            Assert.That(method, Is.Not.Null);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(YooAsset.SceneHandle)));

            ParameterInfo activationParameter = method.GetParameters()[3];
            Assert.That(activationParameter.Name, Is.EqualTo("allowSceneActivation"));
            Assert.That(activationParameter.HasDefaultValue, Is.True);
            Assert.That(activationParameter.DefaultValue, Is.EqualTo(true));
            Assert.That(
                typeof(YooAsset.SceneHandle).GetMethod(
                    nameof(YooAsset.SceneHandle.AllowSceneActivation),
                    Type.EmptyTypes),
                Is.Not.Null);
        }

        [Test]
        public void NativeOptions_PreserveQualifiedMaximumConcurrency()
        {
            var initializationOptions = new HostPlayModeOptions
            {
                BundleLoadingMaxConcurrency = QualifiedMaximumConcurrency,
            };
            var resourceDownloaderOptions = new ResourceDownloaderOptions(
                QualifiedMaximumConcurrency,
                retryCount: 3);
            var bundleDownloaderOptions = new BundleDownloaderOptions(
                Array.Empty<AssetInfo>(),
                downloadDependencies: true,
                maximumConcurrency: QualifiedMaximumConcurrency,
                retryCount: 3);

            Assert.That(
                initializationOptions.BundleLoadingMaxConcurrency,
                Is.EqualTo(QualifiedMaximumConcurrency));
            Assert.That(
                resourceDownloaderOptions.MaximumConcurrency,
                Is.EqualTo(QualifiedMaximumConcurrency));
            Assert.That(
                bundleDownloaderOptions.MaximumConcurrency,
                Is.EqualTo(QualifiedMaximumConcurrency));
        }

        [Test]
        public void RawFileObject_SeparatesOwnedCopyFromBorrowedSpan()
        {
            MethodInfo createMethod = typeof(RawFileObject).GetMethod(
                "CreateFromBytes",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo releaseMethod = typeof(RawFileObject).GetMethod(
                "Release",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(createMethod, Is.Not.Null);
            Assert.That(releaseMethod, Is.Not.Null);

            var source = new byte[] { 1, 2, 3, 4 };
            var rawFile = (RawFileObject)createMethod.Invoke(null, new object[] { source });
            Assert.That(rawFile, Is.Not.Null);
            try
            {
                byte[] ownedCopy = rawFile.GetBytes();
                Assert.That(ownedCopy, Is.Not.SameAs(source));
                CollectionAssert.AreEqual(source, ownedCopy);

                ownedCopy[0] = 99;
                Assert.That(rawFile.GetBytesAsReadOnlySpan()[0], Is.EqualTo(1));

                releaseMethod.Invoke(rawFile, null);
                Assert.That(rawFile.GetBytes(), Is.Null);
                Assert.That(rawFile.GetBytesAsReadOnlySpan().IsEmpty, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rawFile);
            }
        }
    }
}
