using System;
using System.IO;
using System.Linq;
using Build.Pipeline.Integrations.YooAsset3.Publication;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3.Tests
{
    public sealed class YooAsset3PlayerSessionTests
    {
        private const string InvocationId = "p1";
        private string projectRoot;
        private string testRoot;
        private string buildOutputRoot;
        private string bundledFileRoot;

        [SetUp]
        public void SetUp()
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            // Keep every scratch path segment minimal. YooAsset3BuildPlan reserves a fixed suffix for the
            // publication staging directory and for YooAsset-generated child paths, and the resolved output
            // path still has to fit the Win32 MAX_PATH budget. The staging directory alone consumes 48
            // characters of that reservation, so a deep scratch root makes the plan reject every test before
            // the behaviour under test ever runs.
            testRoot = Path.Combine(
                unityProjectRoot,
                "Temp",
                "bpt",
                Guid.NewGuid().ToString("N").Substring(0, 6));
            projectRoot = testRoot;
            buildOutputRoot = Path.Combine(projectRoot, "BO");
            bundledFileRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "YooAsset");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "StreamingAssets"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }

        [Test]
        public void PlayerSession_DoesNotClaimProcessGlobalExclusivity()
        {
            var adapter = new YooAsset3BuildAdapter();

            Assert.That(adapter.ExclusivePlayerSessionKey, Is.Empty);
        }

        [Test]
        public void PlayerBuildSession_HidesAndRestoresPublicationArtifactsAroundPlayerBuild()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            WriteOwnedPublication(plan.Packages[0], true, "payload.txt", "old-bundle");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            try
            {
                transaction.Prepare();
                PublicationJournalOperation output = transaction.Packages[0].OutputOperation;
                PublicationJournalOperation bundled = transaction.Packages[0].BundledOperation;
                WriteFile(output.stage, "payload.txt", "new-output");
                WriteFile(bundled.stage, "payload.txt", "new-bundle");
                transaction.SealReadyDirectories();
                transaction.ActivateDownstreamInputs(NoOp);

                // The deferred publication is deliberately not disposed here: its
                // Dispose path uses AssetDatabase.Refresh, which the filesystem
                // test must avoid. The transaction is aborted directly instead.
                var deferred = new YooAsset3BuildAdapter.YooAsset3DeferredPublication(transaction, NoOp);
                string markerPath = Path.Combine(
                    bundled.target,
                    PublicationOwnership.MarkerFileName);

                Assert.That(File.Exists(markerPath), Is.True, "precondition: published marker exists in the bundled target");
                Assert.That(Directory.Exists(bundled.backup), Is.True, "precondition: backup directory exists");
                Assert.That(File.Exists(bundled.protectedMeta), Is.True, "precondition: protected sibling meta exists");

                using (new YooAsset3BuildAdapter.YooAsset3DeferredPublication.PlayerBuildSession(deferred))
                {
                    Assert.That(File.Exists(markerPath), Is.False, "marker must be relocated out of StreamingAssets during the Player build");
                    Assert.That(Directory.Exists(bundled.backup), Is.False, "backup must be relocated out of StreamingAssets during the Player build");
                    Assert.That(File.Exists(bundled.protectedMeta), Is.False, "protected meta must be relocated out of StreamingAssets during the Player build");
                }

                Assert.That(File.Exists(markerPath), Is.True, "marker must be restored after the Player build");
                Assert.That(Directory.Exists(bundled.backup), Is.True, "backup must be restored after the Player build");
                Assert.That(File.Exists(bundled.protectedMeta), Is.True, "protected meta must be restored after the Player build");
            }
            finally
            {
                try
                {
                    transaction.Abort(NoOp);
                }
                catch (Exception abortFailure)
                {
                    // Cleanup must not mask the failure that ran first. Record it and let the original
                    // exception reach the runner.
                    TestContext.WriteLine($"Abort during cleanup failed: {abortFailure}");
                }
            }
        }

        [Test]
        public void PlayerBuildSession_RestoreFailureIsRetryable()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            WriteOwnedPublication(plan.Packages[0], true, "payload.txt", "old-bundle");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            // Relocation is rooted at the transaction's project root, which is the scratch project here.
            // Deriving the path from Application.dataPath pointed the test at the real Unity project's Temp
            // directory, which the product never writes to, so no relocated artifact was ever discovered.
            string relocationRoot = RelocationJournalStore.GetRelocationRoot(projectRoot);
            Directory.CreateDirectory(relocationRoot);
            try
            {
                transaction.Prepare();
                PublicationJournalOperation output = transaction.Packages[0].OutputOperation;
                PublicationJournalOperation bundled = transaction.Packages[0].BundledOperation;
                WriteFile(output.stage, "payload.txt", "new-output");
                WriteFile(bundled.stage, "payload.txt", "new-bundle");
                transaction.SealReadyDirectories();
                transaction.ActivateDownstreamInputs(NoOp);

                var deferred = new YooAsset3BuildAdapter.YooAsset3DeferredPublication(transaction, NoOp);
                string markerPath = Path.Combine(
                    bundled.target,
                    PublicationOwnership.MarkerFileName);
                Assert.That(File.Exists(markerPath), Is.True, "precondition: published marker exists");

                string[] relocatedBefore = Directory.GetFiles(
                    relocationRoot,
                    "*.file",
                    SearchOption.TopDirectoryOnly);
                var session = new YooAsset3BuildAdapter.YooAsset3DeferredPublication.PlayerBuildSession(deferred);
                string relocatedMarker = Directory.GetFiles(
                        relocationRoot,
                        "*.file",
                        SearchOption.TopDirectoryOnly)
                    .Where(path => !relocatedBefore.Contains(path, StringComparer.Ordinal))
                    .FirstOrDefault(IsOwnershipMarker);
                Assert.That(relocatedMarker, Is.Not.Null, "the relocated ownership marker must be discoverable");
                Assert.That(File.Exists(markerPath), Is.False, "marker must be relocated during the Player build");

                using (new FileStream(
                           relocatedMarker,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.None))
                {
                    Assert.Throws<AggregateException>(() => session.Dispose());
                }

                Assert.That(File.Exists(markerPath), Is.False, "marker stays relocated after a failed restore");

                Assert.DoesNotThrow(() => session.Dispose());
                Assert.That(File.Exists(markerPath), Is.True, "marker is restored after the lock is released");
                Assert.That(Directory.Exists(bundled.backup), Is.True, "backup is restored");
                Assert.That(File.Exists(bundled.protectedMeta), Is.True, "protected meta is restored");
            }
            finally
            {
                try
                {
                    transaction.Abort(NoOp);
                }
                catch (Exception abortFailure)
                {
                    // Cleanup must not mask the failure that ran first. Record it and let the original
                    // exception reach the runner.
                    TestContext.WriteLine($"Abort during cleanup failed: {abortFailure}");
                }
            }
        }

        private static bool IsOwnershipMarker(string path)
        {
            string content = File.ReadAllText(path);
            return content.IndexOf("yooasset-publication-owner", StringComparison.Ordinal) >= 0;
        }

        [Test]
        public void BundledTargetOutsideStreamingAssets_IsRejectedByJournalRootValidation()
        {
            // The bundled file root is YooAsset's built-in resource location, so it has to live under
            // Assets/StreamingAssets. A root outside that tree would publish content the Player build cannot
            // read at runtime, so the journal refuses to record it instead of tolerating a bundled target that
            // has no downstream meaning.
            string externalBundledRoot = Path.Combine(projectRoot, "Assets", "NotStreamingAssets");
            Directory.CreateDirectory(externalBundledRoot);
            YooAsset3BuildPlan plan = CreatePlan(
                externalBundledRoot,
                CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll, externalBundledRoot));
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => transaction.Prepare());
            StringAssert.Contains("approved project locations", exception.Message);
        }

        private YooAsset3BuildPlan CreatePlan(params YooAsset3PackageBuildPlan[] packages)
        {
            return CreatePlan(bundledFileRoot, packages);
        }

        private YooAsset3BuildPlan CreatePlan(string bundledRoot, params YooAsset3PackageBuildPlan[] packages)
        {
            return new YooAsset3BuildPlan(
                projectRoot,
                buildOutputRoot,
                bundledRoot,
                packages,
                Array.Empty<string>());
        }

        private YooAsset3PackageBuildPlan CreatePackage(string packageName, EBundledCopyOption bundledCopyOption)
        {
            return CreatePackage(packageName, bundledCopyOption, bundledFileRoot);
        }

        private YooAsset3PackageBuildPlan CreatePackage(
            string packageName,
            EBundledCopyOption bundledCopyOption,
            string bundledRoot)
        {
            var profile = new YooAssetPackageProfile
            {
                packageName = packageName,
                buildPipeline = YooAssetBuildPipelineKind.RawFile,
                bundledCopyOption = ToProfileOption(bundledCopyOption),
                versionCollisionPolicy = YooAssetVersionCollisionPolicy.ReplaceExactVersion
            };
            var parameters = new RawFileBuildParameters
            {
                BuildOutputRoot = buildOutputRoot,
                BundledFileRoot = bundledRoot,
                BuildPipeline = EBuildPipeline.RawFileBuildPipeline.ToString(),
                BuildBundleType = (int)EBundleType.RawBundle,
                BuildTarget = BuildTarget.StandaloneWindows64,
                PackageName = packageName,
                PackageVersion = "1.0.0",
                PackageNote = "player-session-test",
                BundledCopyOption = bundledCopyOption
            };
            return new YooAsset3PackageBuildPlan(
                profile,
                parameters,
                new UnusedBuildPipeline(),
                string.Empty,
                YooAssetCryptographyIdentity.NoneAdapterId,
                YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId);
        }

        private static YooAssetBundledCopyOption ToProfileOption(EBundledCopyOption option)
        {
            switch (option)
            {
                case EBundledCopyOption.None:
                    return YooAssetBundledCopyOption.None;
                case EBundledCopyOption.OnlyCopyAll:
                    return YooAssetBundledCopyOption.OnlyCopyAll;
                case EBundledCopyOption.OnlyCopyByTags:
                    return YooAssetBundledCopyOption.OnlyCopyByTags;
                default:
                    throw new ArgumentOutOfRangeException(nameof(option), option, null);
            }
        }

        private static void WriteFile(string directory, string fileName, string content)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), content);
        }

        private void WriteOwnedPublication(
            YooAsset3PackageBuildPlan package,
            bool bundled,
            string fileName,
            string content)
        {
            string directory = bundled ? package.BundledPackageDirectory : package.OutputPackageDirectory;
            string kind = bundled
                ? PublicationOwnership.BundledPackageKind
                : PublicationOwnership.PackageOutputKind;
            WriteFile(directory, fileName, content);
            if (bundled)
            {
                File.WriteAllText(
                    directory + ".meta",
                    "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nfolderAsset: yes\n");
            }

            PublicationOwnership.Seal(
                projectRoot,
                directory,
                kind,
                package.PackageName,
                package.PackageVersion,
                YooAssetCryptographyIdentity.NoneAdapterId,
                YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId,
                Guid.NewGuid().ToString("N"),
                UnityJournalSerializer.Instance);
        }

        private static void NoOp()
        {
        }

        private sealed class UnusedBuildPipeline : IBuildPipeline
        {
            public BuildResult Run(BuildParameters buildParameters, bool enableLog)
            {
                throw new InvalidOperationException("The Player build session tests do not execute YooAsset.");
            }
        }
    }
}
