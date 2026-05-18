using System.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using NUnit.Framework;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetManagerTests
    {
        [TearDown]
        public void TearDown()
        {
            AssetManagementLocator.DefaultPackage = null;
        }

        [Test]
        public async Task InitializeDefaultPackageAsync_Initializes_Module_Creates_Package_And_Sets_Locator()
        {
            var module = new RecordingAssetModule();

            IAssetPackage package = await AssetManager.InitializeDefaultPackageAsync(
                module,
                "Default",
                new AssetManagementOptions(operationSystemMaxTimeSliceMs: 1, bundleLoadingMaxConcurrency: 8),
                new AssetPackageInitOptions(AssetPlayMode.Offline, providerOptions: null));

            Assert.AreSame(module.CreatedPackage, package);
            Assert.AreSame(package, AssetManagementLocator.DefaultPackage);
            Assert.AreEqual(1, module.InitializeCallCount);
            Assert.AreEqual(1, module.CreatePackageCallCount);
            Assert.AreEqual(1, module.CreatedPackage.InitializeCallCount);
            Assert.AreEqual(10, module.LastOptions.OperationSystemMaxTimeSliceMs);
            Assert.AreEqual(8, module.LastOptions.BundleLoadingMaxConcurrency);
        }

        [Test]
        public async Task InitializeDefaultPackageAsync_Reuses_Existing_Package_And_Skips_Initialized_Module()
        {
            var module = new RecordingAssetModule { InitializedValue = true };
            module.CreatedPackage = new RecordingAssetPackage { NameValue = "Default" };

            IAssetPackage package = await AssetManager.InitializeDefaultPackageAsync(
                module,
                "Default",
                default,
                new AssetPackageInitOptions(AssetPlayMode.EditorSimulate, providerOptions: null));

            Assert.AreSame(module.CreatedPackage, package);
            Assert.AreEqual(0, module.InitializeCallCount);
            Assert.AreEqual(0, module.CreatePackageCallCount);
            Assert.AreEqual(1, module.CreatedPackage.InitializeCallCount);
        }
    }
}
