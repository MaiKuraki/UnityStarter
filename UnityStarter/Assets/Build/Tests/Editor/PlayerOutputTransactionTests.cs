using System;
using System.IO;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class PlayerOutputTransactionTests
    {
        private string sandboxRoot;
        private string projectRoot;
        private string buildRoot;
        private string outputDirectory;
        private string outputPath;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "BuildPipelinePlayerTransactionTests-" + Guid.NewGuid().ToString("N"));
            projectRoot = Path.Combine(sandboxRoot, "UnityProject");
            buildRoot = Path.Combine(projectRoot, "Build");
            outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            outputPath = Path.Combine(outputDirectory, "TestProduct.exe");
            Directory.CreateDirectory(projectRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(sandboxRoot))
            {
                Directory.Delete(sandboxRoot, true);
            }
        }

        [Test]
        public void DisposeBeforeCommit_PreservesLastKnownGoodOutput()
        {
            WriteOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);

            using (PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(request))
            {
                Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
                File.WriteAllText(transaction.StageOutputPath, "partial");
            }

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            Assert.That(Directory.Exists(GetStateRoot()), Is.True);
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);
        }

        [Test]
        public void Commit_ReplacesOutputOnlyAfterStageIsReady()
        {
            WriteOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);

            using (PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(request))
            {
                File.WriteAllText(transaction.StageOutputPath, "new");
                Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
                transaction.Commit();
            }

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("new"));
            Assert.That(
                File.Exists(outputDirectory + ".buildpipeline-player-owner.json"),
                Is.True);
        }

        [Test]
        public void IncrementalCommit_StagesPriorOutputWithoutMutatingIt()
        {
            WriteOutput("old");
            string retainedPath = Path.Combine(outputDirectory, "Data", "retained.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(retainedPath));
            File.WriteAllText(retainedPath, "retained");
            BuildRequest request = CreateRequest(BuildIncrementality.Incremental);

            using (PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(request))
            {
                Assert.That(File.ReadAllText(transaction.StageOutputPath), Is.EqualTo("old"));
                Assert.That(
                    File.ReadAllText(Path.Combine(
                        Path.GetDirectoryName(transaction.StageOutputPath),
                        "Data",
                        "retained.bin")),
                    Is.EqualTo("retained"));
                File.WriteAllText(transaction.StageOutputPath, "new");
                transaction.Commit();
            }

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(retainedPath), Is.EqualTo("retained"));
        }

        [Test]
        public void DisposeAfterBackupMoveFault_RestoresOriginalOutput()
        {
            WriteOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(
                request,
                checkpoint =>
                {
                    if (checkpoint == PlayerOutputTransaction.BackupMovedCheckpoint)
                    {
                        throw new InvalidOperationException("Injected backup-move fault.");
                    }
                });
            File.WriteAllText(transaction.StageOutputPath, "new");

            Assert.Throws<InvalidOperationException>(() => transaction.Commit());
            Assert.DoesNotThrow(() => transaction.Dispose());

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);
        }

        [Test]
        public void Dispose_WhenUnreadyStageOwnershipIsRemoved_FailsClosed()
        {
            WriteOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(request);
            File.Delete(Path.Combine(
                transaction.StageRoot,
                ".buildpipeline-player-stage-anchor"));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => transaction.Dispose());

            StringAssert.Contains("recover", exception.Message.ToLowerInvariant());
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            Assert.That(Directory.Exists(transaction.StageRoot), Is.True);
        }

        [Test]
        public void Begin_WhenPublishedOwnerPathContainsForeignFile_FailsClosedAndReleasesLock()
        {
            WriteOutput("old");
            string ownerPath = outputDirectory + ".buildpipeline-player-owner.json";
            const string foreignContents = "{\"external\":\"owned-by-another-tool\"}";
            File.WriteAllText(ownerPath, foreignContents);
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => PlayerOutputTransaction.Begin(request));

            Assert.That(exception.Message, Does.Contain("ownership marker"));
            Assert.That(File.ReadAllText(ownerPath), Is.EqualTo(foreignContents));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);

            File.Delete(ownerPath);
            Assert.DoesNotThrow(() =>
            {
                using (PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(request))
                {
                }
            });
        }

        [Test]
        public void Begin_WhenPublishedOwnerMatchesOutput_AllowsNextTransaction()
        {
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            using (PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(request))
            {
                File.WriteAllText(transaction.StageOutputPath, "published");
                transaction.Commit();
            }

            Assert.DoesNotThrow(() =>
            {
                using (PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(request))
                {
                }
            });
        }

        [Test]
        public void FolderArtifactStage_PreservesFinalAppBundleName()
        {
            outputDirectory = Path.Combine(buildRoot, "macOS", "Release", "TestProduct.app")
                              + Path.DirectorySeparatorChar;
            outputPath = outputDirectory;
            BuildRequest request = CreateRequest(
                incrementality: BuildIncrementality.Clean,
                target: BuildTarget.StandaloneOSX,
                outputIsFolder: true);

            using (PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(request))
            {
                Assert.That(
                    Path.GetFileName(transaction.StageOutputPath),
                    Is.EqualTo("TestProduct.app"));
                string stagedInfo = Path.Combine(
                    transaction.StageOutputPath,
                    "Contents",
                    "Info.plist");
                Directory.CreateDirectory(Path.GetDirectoryName(stagedInfo));
                File.WriteAllText(stagedInfo, "plist");
                transaction.Commit();
            }

            Assert.That(
                File.ReadAllText(Path.Combine(outputDirectory, "Contents", "Info.plist")),
                Is.EqualTo("plist"));
        }

        [Test]
        public void Begin_WhenPlayerStageCannotFitLegacyWindowsBudget_FailsBeforeJournalAndReleasesLock()
        {
            const int desiredFinalDirectoryLength = 180;
            int leafLength = desiredFinalDirectoryLength
                - Path.GetFullPath(buildRoot).Length
                - 1;
            Assert.That(leafLength, Is.GreaterThan(0));
            outputDirectory = Path.Combine(buildRoot, new string('p', leafLength));
            outputPath = Path.Combine(outputDirectory, "TestProduct.exe");
            BuildRequest longPathRequest = CreateRequest(BuildIncrementality.Clean);

            Assert.Throws<PathTooLongException>(() =>
                PlayerOutputTransaction.Begin(longPathRequest));
            Assert.That(
                File.Exists(Path.Combine(GetStateRoot(), "active.json")),
                Is.False);

            outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            outputPath = Path.Combine(outputDirectory, "TestProduct.exe");
            Assert.DoesNotThrow(() =>
            {
                using (PlayerOutputTransaction transaction =
                       PlayerOutputTransaction.Begin(CreateRequest(BuildIncrementality.Clean)))
                {
                }
            });
        }

        private BuildRequest CreateRequest(
            BuildIncrementality incrementality,
            BuildTarget target = BuildTarget.StandaloneWindows64,
            bool outputIsFolder = false)
        {
            return new BuildRequest(
                "TestCompany",
                "TestProduct",
                "com.example.test",
                "Assets/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                null,
                target,
                BuildRequestFactory.GetNamedBuildTarget(target),
                ScriptingImplementation.Mono2x,
                projectRoot,
                buildRoot,
                outputPath,
                outputDirectory,
                outputIsFolder: outputIsFolder,
                incrementality: incrementality,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: true,
                applicationVersion: "1.0.0",
                assetContentProviderId: string.Empty,
                assetContentConfiguration: null,
                useHybridClr: false,
                enablePlayerObfuscation: false,
                stepIds: new[] { BuildStepIds.Player });
        }

        private void WriteOutput(string contents)
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(outputPath, contents);
        }

        private string GetStateRoot()
        {
            return Path.Combine(
                projectRoot,
                ".buildpipeline",
                "transactions",
                "player");
        }
    }
}
