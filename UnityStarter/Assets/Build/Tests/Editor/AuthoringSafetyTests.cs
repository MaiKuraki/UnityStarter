using System;
using System.IO;
using Build.Pipeline.Editor;
using NUnit.Framework;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class AuthoringSafetyTests
    {
        [Test]
        public void YooAssetPackageCatalog_PreservesExactValidIdentities()
        {
            bool valid = YooAssetPackageAuthoringCatalog.TryValidatePackageNames(
                new[] { "release_content", "DefaultPackage" },
                out string[] packageNames,
                out string diagnostic);

            Assert.That(valid, Is.True, diagnostic);
            CollectionAssert.AreEqual(
                new[] { "DefaultPackage", "release_content" },
                packageNames);
        }

        [TestCase(" DefaultPackage")]
        [TestCase("DefaultPackage ")]
        [TestCase("package name")]
        [TestCase("../escape")]
        public void YooAssetPackageCatalog_RejectsInsteadOfNormalizingInvalidIdentity(
            string packageName)
        {
            bool valid = YooAssetPackageAuthoringCatalog.TryValidatePackageNames(
                new[] { packageName },
                out string[] packageNames,
                out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(packageNames, Is.Empty);
            Assert.That(diagnostic, Does.Contain("invalid stable package name"));
        }

        [Test]
        public void YooAssetPackageCatalog_RejectsCaseInsensitiveIdentityCollision()
        {
            bool valid = YooAssetPackageAuthoringCatalog.TryValidatePackageNames(
                new[] { "DefaultPackage", "defaultpackage" },
                out string[] packageNames,
                out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(packageNames, Is.Empty);
            Assert.That(diagnostic, Does.Contain("case-insensitively"));
        }

        [Test]
        public void YooAssetPackageCatalog_RejectsBlankIdentity()
        {
            bool valid = YooAssetPackageAuthoringCatalog.TryValidatePackageNames(
                new[] { "   " },
                out string[] packageNames,
                out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(packageNames, Is.Empty);
            Assert.That(diagnostic, Does.Contain("empty package name"));
        }

        [Test]
        public void YooAssetSettingCatalog_RequiresExactlyOneAsset()
        {
            YooAssetPackageCatalogStatus missing =
                YooAssetPackageAuthoringCatalog.ValidateSettingAssetCatalog(
                    Array.Empty<string>(),
                    out string missingDiagnostic);
            YooAssetPackageCatalogStatus duplicate =
                YooAssetPackageAuthoringCatalog.ValidateSettingAssetCatalog(
                    new[] { "Assets/Config/A.asset", "Assets/Config/B.asset" },
                    out string duplicateDiagnostic);
            YooAssetPackageCatalogStatus ready =
                YooAssetPackageAuthoringCatalog.ValidateSettingAssetCatalog(
                    new[] { "Assets/Config/BundleCollectorSetting.asset" },
                    out string readyDiagnostic);

            Assert.That(missing, Is.EqualTo(YooAssetPackageCatalogStatus.SettingsMissing));
            Assert.That(missingDiagnostic, Does.Contain("no Bundle Collector settings asset"));
            Assert.That(duplicate, Is.EqualTo(YooAssetPackageCatalogStatus.Invalid));
            Assert.That(duplicateDiagnostic, Does.Contain("Exactly one"));
            Assert.That(ready, Is.EqualTo(YooAssetPackageCatalogStatus.Ready));
            Assert.That(readyDiagnostic, Is.Empty);
        }

        [Test]
        public void VersionInfoTargetOccupation_ReportsWrongAssetTypeFileAndMeta()
        {
            const string AssetPath = "Assets/Resources/VersionInfoData.asset";

            string wrongType = BuildDataEditor.DescribeVersionInfoTargetOccupation(
                AssetPath,
                containsVersionInfoAsset: false,
                occupyingAssetType: "Texture2D",
                targetFileExists: true,
                targetDirectoryExists: false,
                targetMetaExists: true);
            string rawFile = BuildDataEditor.DescribeVersionInfoTargetOccupation(
                AssetPath,
                containsVersionInfoAsset: false,
                occupyingAssetType: null,
                targetFileExists: true,
                targetDirectoryExists: false,
                targetMetaExists: true);
            string orphanMeta = BuildDataEditor.DescribeVersionInfoTargetOccupation(
                AssetPath,
                containsVersionInfoAsset: false,
                occupyingAssetType: null,
                targetFileExists: false,
                targetDirectoryExists: false,
                targetMetaExists: true);

            Assert.That(wrongType, Does.Contain("Texture2D"));
            Assert.That(rawFile, Does.Contain("cannot load as VersionInfoData"));
            Assert.That(orphanMeta, Does.Contain("orphan .meta"));
        }

        [Test]
        public void VersionInfoTargetOccupation_AcceptsExpectedAsset()
        {
            string error = BuildDataEditor.DescribeVersionInfoTargetOccupation(
                "Assets/Resources/VersionInfoData.asset",
                containsVersionInfoAsset: true,
                occupyingAssetType: null,
                targetFileExists: true,
                targetDirectoryExists: false,
                targetMetaExists: true);

            Assert.That(error, Is.Null);
        }

        [Test]
        public void RuntimeVersionInfoPath_RequiresExactResourcesDirectory()
        {
            Assert.DoesNotThrow(() => RuntimeVersionInfoPathPolicy.Validate(
                "Assets/Build/Runtime/Resources/VersionInfoData.asset"));

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                RuntimeVersionInfoPathPolicy.Validate(
                    "Assets/Build/Runtime/Generated/VersionInfoData.asset"));
            Assert.That(exception.Message, Does.Contain("Resources"));
        }

        [Test]
        public void ProjectRelativeDirectory_UsesHostFileSystemCasingRule()
        {
            string parent = Path.Combine(Path.GetTempPath(), "BuildAuthoringPathFieldCase");
            string projectRoot = Path.Combine(parent, "ProjectRoot");
            string casingAlias = Path.Combine(parent, "projectroot", "Assets");

            bool accepted = BuildAuthoringPathField.TryMakeProjectRelative(
                projectRoot,
                casingAlias,
                out string relative);

            bool usesWindowsPathSemantics = Path.DirectorySeparatorChar == '\\';
            Assert.That(accepted, Is.EqualTo(usesWindowsPathSemantics));
            if (usesWindowsPathSemantics)
            {
                Assert.That(relative, Is.EqualTo("Assets"));
            }
            else
            {
                Assert.That(relative, Is.Null);
            }
        }

        [Test]
        public void YooAssetBuildRootPolicy_DefaultAndExplicitRootsUseSharedSafetyRules()
        {
            string projectRoot = BuildAuthoringPathField.GetProjectRoot();

            string buildRoot = YooAssetBuildRootPolicy.ResolveBuildOutputRoot(
                projectRoot,
                string.Empty);
            string bundledRoot = YooAssetBuildRootPolicy.ResolveConfiguredBundledFileRoot(
                projectRoot,
                "Assets/StreamingAssets/YooAsset");
            string normalizedBuildRoot = YooAssetBuildRootPolicy.ResolveBuildOutputRoot(
                projectRoot,
                "Bundles\\Release");

            Assert.That(
                buildRoot,
                Is.EqualTo(Path.GetFullPath(Path.Combine(projectRoot, "Bundles"))));
            Assert.That(
                bundledRoot,
                Is.EqualTo(Path.GetFullPath(
                    Path.Combine(projectRoot, "Assets", "StreamingAssets", "YooAsset"))));
            Assert.That(
                normalizedBuildRoot,
                Is.EqualTo(Path.GetFullPath(
                    Path.Combine(projectRoot, "Bundles", "Release"))));
            Assert.Throws<InvalidOperationException>(() =>
                YooAssetBuildRootPolicy.ResolveConfiguredBundledFileRoot(
                    projectRoot,
                    "Assets/Generated/YooAsset"));
        }

        [Test]
        public void YooAssetBuildRootPolicy_RejectsPortableCasingOverlap()
        {
            string projectRoot = BuildAuthoringPathField.GetProjectRoot();
            string buildRoot = Path.Combine(projectRoot, "Bundles");
            string casingAliasChild = Path.Combine(projectRoot, "bundles", "BuiltIn");

            Assert.Throws<InvalidOperationException>(() =>
                YooAssetBuildRootPolicy.EnsureRootsDoNotOverlap(
                    buildRoot,
                    casingAliasChild));
        }
    }
}
