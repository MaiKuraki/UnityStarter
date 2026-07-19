using System;
using System.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using NUnit.Framework;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetManagerTests
    {
        [Test]
        public async Task InitializeDefaultPackageAsync_Initializes_Module_Creates_And_Returns_Package()
        {
            var module = new RecordingAssetModule();

            IAssetPackage package = await AssetManager.InitializeDefaultPackageAsync(
                module,
                "Default",
                new AssetManagementOptions(new AssetCacheTuning(3, 7, 32L * 1024 * 1024)),
                new AssetPackageInitOptions());

            Assert.AreSame(module.CreatedPackage, package);
            Assert.AreEqual(1, module.InitializeCallCount);
            Assert.AreEqual(1, module.CreatePackageCallCount);
            Assert.AreEqual(1, module.CreatedPackage.InitializeCallCount);
            Assert.AreEqual(3, module.LastOptions.DefaultCacheTuning.ProbationEntryLimit);
            Assert.AreEqual(7, module.LastOptions.DefaultCacheTuning.ProtectedEntryLimit);
            Assert.AreEqual(32L * 1024 * 1024, module.LastOptions.DefaultCacheTuning.IdleByteBudget);
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
                new AssetPackageInitOptions());

            Assert.AreSame(module.CreatedPackage, package);
            Assert.AreEqual(0, module.InitializeCallCount);
            Assert.AreEqual(0, module.CreatePackageCallCount);
            Assert.AreEqual(1, module.CreatedPackage.InitializeCallCount);
        }

        [Test]
        public void InitializeDefaultPackageAsync_Throws_When_Package_Initialization_Fails()
        {
            var module = new RecordingAssetModule
            {
                CreatedPackage = new RecordingAssetPackage
                {
                    NameValue = "Default",
                    InitializeResult = false,
                },
            };

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await AssetManager.InitializeDefaultPackageAsync(
                    module,
                    "Default",
                    default,
                    new AssetPackageInitOptions()));
        }

        [Test]
        public void InitializeDefaultPackageAsync_Removes_New_Package_When_Initialization_Fails()
        {
            var module = new RecordingAssetModule
            {
                CreatedPackageInitializeResult = false
            };

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await AssetManager.InitializeDefaultPackageAsync(
                    module,
                    "Default",
                    default,
                    new AssetPackageInitOptions()));

            Assert.AreEqual(1, module.CreatePackageCallCount);
            Assert.AreEqual(1, module.RemovePackageCallCount);
            Assert.IsNull(module.CreatedPackage);
        }
    }
}
