using System;
using System.IO;
using System.Text;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class HybridCLRReleaseBaselineTests
    {
        private string sandboxRoot;
        private string projectRoot;
        private string buildRoot;
        private string aotSource;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "h" + Guid.NewGuid().ToString("N").Substring(0, 8));
            projectRoot = sandboxRoot;
            buildRoot = Path.Combine(projectRoot, "Build");
            aotSource = Path.Combine(projectRoot, "HybridCLRData", "AOT", "Windows");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
            Directory.CreateDirectory(buildRoot);
            Directory.CreateDirectory(aotSource);
            WriteAOT("mscorlib.dll", "aot-v1");
            WriteAOT("System.dll", "system-v1");
        }

        [TearDown]
        public void TearDown()
        {
            if (string.IsNullOrWhiteSpace(sandboxRoot) || !Directory.Exists(sandboxRoot))
            {
                return;
            }

            string expectedParent = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(sandboxRoot);
            Assert.That(Path.GetDirectoryName(candidate), Is.EqualTo(expectedParent));
            string directoryName = Path.GetFileName(candidate);
            Assert.That(directoryName, Does.StartWith("h"));
            Assert.That(directoryName.Length, Is.EqualTo(9));
            Assert.That(
                long.TryParse(
                    directoryName.Substring(1),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _),
                Is.True);
            Directory.Delete(candidate, recursive: true);
        }

        [Test]
        public void Commit_PublishesHashedTargetIsolatedBaseline()
        {
            HybridCLRReleaseBaselineExpectation expectation = CreateExpectation();
            using (HybridCLRReleaseBaselineTransaction transaction =
                   HybridCLRReleaseBaselineTransaction.Stage(
                       expectation,
                       "player",
                       aotSource,
                       CreateVersion()))
            {
                transaction.CommitForTesting();
            }

            HybridCLRReleaseBaseline baseline =
                HybridCLRReleaseBaselineStore.ValidateAndResolve(expectation);
            Assert.That(Directory.Exists(baseline.AOTDirectory), Is.True);
            Assert.That(
                File.ReadAllText(Path.Combine(baseline.AOTDirectory, "mscorlib.dll")),
                Is.EqualTo("aot-v1"));
            string manifest = File.ReadAllText(Path.Combine(
                baseline.Directory,
                HybridCLRReleaseBaselineStore.ManifestFileName));
            StringAssert.Contains("\"formatVersion\": 1", manifest);
            StringAssert.Contains("\"buildConfiguration\": \"Release\"", manifest);
            StringAssert.Contains("\"playerInvocationId\": \"player\"", manifest);
            StringAssert.Contains("\"sha256\"", manifest);
            Assert.That(
                File.Exists(
                    HybridCLRReleaseBaselineTransaction.GetActiveJournalPathForTesting(
                        projectRoot)),
                Is.False);
        }

        [Test]
        public void ValidateAndResolve_WhenAotFileIsTampered_FailsClosed()
        {
            HybridCLRReleaseBaselineExpectation expectation = PublishBaseline();
            File.WriteAllText(
                Path.Combine(expectation.FinalDirectory, "AOT", "mscorlib.dll"),
                "aot-v2");

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                HybridCLRReleaseBaselineStore.ValidateAndResolve(expectation));
            StringAssert.Contains("hash does not match", exception.Message);
        }

        [Test]
        public void ValidateAndResolve_WhenManifestIsCorrupt_FailsClosed()
        {
            HybridCLRReleaseBaselineExpectation expectation = PublishBaseline();
            File.WriteAllText(
                Path.Combine(
                    expectation.FinalDirectory,
                    HybridCLRReleaseBaselineStore.ManifestFileName),
                "{not-json");

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                HybridCLRReleaseBaselineStore.ValidateAndResolve(expectation));
            StringAssert.Contains("not valid JSON", exception.Message);
        }

        [TestCase("UnityVersion")]
        [TestCase("Backend")]
        [TestCase("Configuration")]
        public void ValidateAndResolve_WhenCompatibilityIdentityChanges_FailsClosed(
            string changedField)
        {
            HybridCLRReleaseBaselineExpectation original = PublishBaseline();
            HybridCLRReleaseBaselineExpectation changed = CreateExpectation(
                finalDirectory: original.FinalDirectory,
                unityVersion: changedField == "UnityVersion" ? "different-unity" : "2022.3.62f3",
                scriptingBackend: changedField == "Backend" ? "Mono2x" : "IL2CPP",
                compatibilityHash: changedField == "Configuration"
                    ? new string('f', 64)
                    : new string('e', 64));

            Assert.Throws<InvalidDataException>(() =>
                HybridCLRReleaseBaselineStore.ValidateAndResolve(changed));
        }

        [Test]
        public void Dispose_AfterPublish_RestoresExactPreviousBaseline()
        {
            HybridCLRReleaseBaselineExpectation expectation = PublishBaseline();
            WriteAOT("mscorlib.dll", "aot-v2");
            WriteAOT("System.dll", "system-v2");

            using (HybridCLRReleaseBaselineTransaction transaction =
                   HybridCLRReleaseBaselineTransaction.Stage(
                       expectation,
                       "player",
                       aotSource,
                       CreateVersion()))
            {
                transaction.Publish();
                Assert.That(
                    File.ReadAllText(Path.Combine(
                        expectation.FinalDirectory,
                        "AOT",
                        "mscorlib.dll")),
                    Is.EqualTo("aot-v2"));
            }

            Assert.That(
                File.ReadAllText(Path.Combine(
                    expectation.FinalDirectory,
                    "AOT",
                    "mscorlib.dll")),
                Is.EqualTo("aot-v1"));
            Assert.DoesNotThrow(() =>
                HybridCLRReleaseBaselineStore.ValidateAndResolve(expectation));
        }

        [Test]
        public void Eligibility_RequiresReleasePlayerWithDirectDependency()
        {
            var hot = new BuildStepInvocation(
                "hot",
                BuildStepTypeIds.HotUpdate);
            var directPlayer = new BuildStepInvocation(
                "player",
                BuildStepTypeIds.Player,
                dependencies: new[]
                {
                    new BuildInvocationDependency("hot")
                });
            BuildExecutionContext direct = CreateContext(
                debugBuild: false,
                hot,
                directPlayer);
            Assert.That(
                HybridCLRReleaseBaselineEligibility
                    .TryGetExplicitReleasePlayerConsumer(
                        direct,
                        hot,
                        out string playerId,
                        out _),
                Is.True);
            Assert.That(playerId, Is.EqualTo("player"));

            var content = new BuildStepInvocation(
                "content",
                BuildStepTypeIds.AssetContent,
                dependencies: new[]
                {
                    new BuildInvocationDependency("hot")
                });
            var transitivePlayer = new BuildStepInvocation(
                "player",
                BuildStepTypeIds.Player,
                dependencies: new[]
                {
                    new BuildInvocationDependency("content")
                });
            BuildExecutionContext transitive = CreateContext(
                debugBuild: false,
                hot,
                content,
                transitivePlayer);
            Assert.That(
                HybridCLRReleaseBaselineEligibility
                    .TryGetExplicitReleasePlayerConsumer(
                        transitive,
                        hot,
                        out _,
                        out _),
                Is.False);

            BuildExecutionContext development = CreateContext(
                debugBuild: true,
                hot,
                directPlayer);
            Assert.That(
                HybridCLRReleaseBaselineEligibility
                    .TryGetExplicitReleasePlayerConsumer(
                        development,
                        hot,
                        out _,
                        out _),
                Is.False);
        }

        [Test]
        public void BaselinePaths_AreIsolatedByTargetAndBackend()
        {
            HybridCLRReleaseBaselineExpectation windows = CreateExpectation();
            HybridCLRReleaseBaselineExpectation android = CreateExpectation(
                finalDirectory: Path.Combine(
                    buildRoot,
                    ".buildpipeline",
                    "baselines",
                    "hybridclr",
                    "Android",
                    "IL2CPP",
                    windows.ReleaseKey),
                buildTarget: "Android");
            HybridCLRReleaseBaselineExpectation mono = CreateExpectation(
                finalDirectory: Path.Combine(
                    buildRoot,
                    ".buildpipeline",
                    "baselines",
                    "hybridclr",
                    "StandaloneWindows64",
                    "Mono2x",
                    windows.ReleaseKey),
                scriptingBackend: "Mono2x");

            Assert.That(android.FinalDirectory, Is.Not.EqualTo(windows.FinalDirectory));
            Assert.That(mono.FinalDirectory, Is.Not.EqualTo(windows.FinalDirectory));
        }

        private HybridCLRReleaseBaselineExpectation PublishBaseline()
        {
            HybridCLRReleaseBaselineExpectation expectation = CreateExpectation();
            using (HybridCLRReleaseBaselineTransaction transaction =
                   HybridCLRReleaseBaselineTransaction.Stage(
                       expectation,
                       "player",
                       aotSource,
                       CreateVersion()))
            {
                transaction.CommitForTesting();
            }

            return expectation;
        }

        private HybridCLRReleaseBaselineExpectation CreateExpectation(
            string finalDirectory = null,
            string buildTarget = "StandaloneWindows64",
            string scriptingBackend = "IL2CPP",
            string unityVersion = "2022.3.62f3",
            string compatibilityHash = null)
        {
            string releaseKey = new string('d', 64);
            string destination = finalDirectory ?? Path.Combine(
                buildRoot,
                ".buildpipeline",
                "baselines",
                "hybridclr",
                buildTarget,
                scriptingBackend,
                releaseKey);
            return new HybridCLRReleaseBaselineExpectation(
                projectRoot,
                buildRoot,
                destination,
                releaseKey,
                "com.test.product",
                "1.2.3",
                "hot",
                buildTarget,
                buildTarget == "Android" ? "Android" : "Standalone",
                scriptingBackend,
                unityVersion,
                "HybridCLR.Editor|8.12.0|test",
                new string('a', 64),
                new string('b', 64),
                new string('c', 64),
                compatibilityHash ?? new string('e', 64),
                new[] { "Game.HotUpdate" });
        }

        private BuildVersionContext CreateVersion()
        {
            return new BuildVersionContext(
                "1.2.3",
                "1.2.3.42",
                42,
                "0123456789abcdef",
                "42",
                "main",
                "2026-08-07T00:00:00Z",
                "git");
        }

        private BuildExecutionContext CreateContext(
            bool debugBuild,
            params BuildStepInvocation[] invocations)
        {
            string outputDirectory = Path.Combine(buildRoot, "Output");
            var request = new BuildRequest(
                "TestCompany",
                "TestProduct",
                "com.test.product",
                "Assets/Generated/VersionInfo.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                projectRoot,
                buildRoot,
                Path.Combine(outputDirectory, "TestProduct.exe"),
                outputDirectory,
                outputIsFolder: false,
                deleteDebugFiles: true,
                debugBuild: debugBuild,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: true,
                applicationVersion: "1.2.3",
                identityOverride: BuildIdentityOverride.Empty,
                steps: invocations);
            var context = new BuildExecutionContext(
                request,
                "test-run",
                new NullEventSink());
            var plan = new CompiledBuildStep[invocations.Length];
            for (int index = 0; index < invocations.Length; index++)
            {
                IBuildStep step;
                switch (invocations[index].StepTypeId)
                {
                    case BuildStepTypeIds.HotUpdate:
                        step = new HotUpdateBuildStep();
                        break;
                    case BuildStepTypeIds.AssetContent:
                        step = new AssetContentBuildStep();
                        break;
                    default:
                        step = new PlayerBuildStep();
                        break;
                }

                plan[index] = new CompiledBuildStep(
                    invocations[index],
                    step,
                    isApplicable: true);
            }

            context.SetPlan(plan);
            return context;
        }

        private void WriteAOT(string fileName, string content)
        {
            File.WriteAllText(
                Path.Combine(aotSource, fileName),
                content,
                new UTF8Encoding(false));
        }

        private sealed class NullEventSink : IBuildEventSink
        {
            public void RunStarted(
                BuildExecutionContext context,
                System.Collections.Generic.IReadOnlyList<CompiledBuildStep> plan)
            {
            }

            public void StepStarted(
                BuildExecutionContext context,
                CompiledBuildStep step)
            {
            }

            public void StepFinished(
                BuildExecutionContext context,
                BuildStepResult result)
            {
            }

            public void RunFinished(
                BuildExecutionContext context,
                BuildRunResult result)
            {
            }
        }
    }
}
