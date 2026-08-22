using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using YooAsset;
using YooAsset.Editor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3.Tests
{
    public sealed class YooAsset3ArtifactEvidenceTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(
                Path.GetTempPath(),
                "BuildPipeline-YooArtifactEvidence-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void StructuredResult_ReportsOnlyBoundedKeyArtifacts()
        {
            const string packageName = "EvidencePackage";
            const string packageVersion = "1.2.3";
            var profile = new YooAssetPackageProfile
            {
                packageName = packageName,
                buildPipeline = YooAssetBuildPipelineKind.RawFile,
                bundledCopyOption = YooAssetBundledCopyOption.None
            };
            var parameters = new RawFileBuildParameters
            {
                BuildOutputRoot = root,
                BundledFileRoot = Path.Combine(root, "StreamingAssets"),
                BuildPipeline = EBuildPipeline.RawFileBuildPipeline.ToString(),
                BuildBundleType = (int)EBundleType.RawBundle,
                BuildTarget = BuildTarget.StandaloneWindows64,
                PackageName = packageName,
                PackageVersion = packageVersion,
                PackageNote = "artifact-evidence-test",
                BundledCopyOption = EBundledCopyOption.None
            };
            var plan = new YooAsset3PackageBuildPlan(
                profile,
                parameters,
                new UnusedBuildPipeline(),
                string.Empty,
                YooAssetCryptographyIdentity.NoneAdapterId,
                YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId);
            Directory.CreateDirectory(plan.OutputPackageDirectory);

            string[] keyArtifacts =
            {
                YooAssetConfiguration.GetBuildReportFileName(packageName, packageVersion),
                YooAssetConfiguration.GetManifestBinaryFileName(packageName, packageVersion),
                YooAssetConfiguration.GetPackageHashFileName(packageName, packageVersion),
                YooAssetConfiguration.GetPackageVersionFileName(packageName)
            };
            foreach (string fileName in keyArtifacts)
            {
                File.WriteAllText(Path.Combine(plan.OutputPackageDirectory, fileName), fileName);
            }

            for (int index = 0; index < 256; index++)
            {
                File.WriteAllText(
                    Path.Combine(plan.OutputPackageDirectory, $"bundle-{index:D4}.bin"),
                    index.ToString());
            }

            AssetContentBuildResult result = new YooAsset3BuildAdapter()
                .CreateSuccessResultForDirectories(
                    plan,
                    plan.OutputPackageDirectory,
                    string.Empty,
                    Array.Empty<string>());

            Assert.That(result.OutputPackageDirectory, Is.EqualTo(Path.GetFullPath(plan.OutputPackageDirectory)));
            Assert.That(result.BundledPackageDirectory, Is.Empty);
            Assert.That(result.ProducedArtifacts.Count, Is.EqualTo(4));
            CollectionAssert.AreEquivalent(
                keyArtifacts.Select(fileName => Path.GetFullPath(
                    Path.Combine(plan.OutputPackageDirectory, fileName))),
                result.ProducedArtifacts);
            Assert.That(
                result.ProducedArtifacts.Any(path => path.EndsWith("bundle-0000.bin", StringComparison.Ordinal)),
                Is.False);
        }

        private sealed class UnusedBuildPipeline : IBuildPipeline
        {
            public BuildResult Run(BuildParameters buildParameters, bool enableLog)
            {
                throw new InvalidOperationException("The evidence test does not execute the native pipeline.");
            }
        }
    }
}
