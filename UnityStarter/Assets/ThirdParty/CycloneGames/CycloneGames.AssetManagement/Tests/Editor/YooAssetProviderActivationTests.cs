using System;

using NUnit.Framework;

using UnityEditor.Compilation;
using UnityEditor.PackageManager;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class YooAssetProviderActivationTests
    {
        private const string PackageId = "com.tuyoogame.yooasset";
        private const string SupportedVersionRange = "[3.0.5,4.0.0)";
        private static readonly Version MinimumSupportedVersion = new Version(3, 0, 5);
        private static readonly Version MaximumSupportedVersionExclusive = new Version(4, 0, 0);
        private const string ProviderAssemblyName =
            "CycloneGames.AssetManagement.Runtime.Providers.YooAsset";
        private const string ProviderTestAssemblyName =
            "CycloneGames.AssetManagement.Providers.YooAsset.Tests.Editor";

        [Test]
        public void RegisteredPackage_ActivatesSupportedProviderAndTests()
        {
            PackageInfo package = FindRegisteredPackage();
            if (package == null)
            {
                Assert.Pass("YooAsset is optional and is not registered in this Unity project.");
                return;
            }

            Assert.That(
                IsSupportedStableVersion(package.version),
                Is.True,
                $"Registered {PackageId} version '{package.version}' is not qualified by the AssetManagement provider. " +
                $"Install a stable version in {SupportedVersionRange}, or qualify and update the provider gate.");

            UnityEditor.Compilation.Assembly[] assemblies =
                CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            Assert.That(
                ContainsAssembly(assemblies, ProviderAssemblyName),
                Is.True,
                $"{PackageId} {package.version} is registered, but provider assembly " +
                $"'{ProviderAssemblyName}' is absent from Unity's active compilation graph.");
            Assert.That(
                ContainsAssembly(assemblies, ProviderTestAssemblyName),
                Is.True,
                $"{PackageId} {package.version} is registered, but provider test assembly " +
                $"'{ProviderTestAssemblyName}' is absent from Unity's active compilation graph.");
        }

        [TestCase(null, false)]
        [TestCase("3.0.4", false)]
        [TestCase("3.0.5-preview.1", false)]
        [TestCase("3.0.5", true)]
        [TestCase("3.0.5+build.1", true)]
        [TestCase("3.1.0", true)]
        [TestCase("3.99.99", true)]
        [TestCase("4.0.0-preview.1", false)]
        [TestCase("4.0.0", false)]
        public void SupportedStableVersionRange_MatchesProviderPolicy(
            string packageVersion,
            bool expected)
        {
            Assert.That(IsSupportedStableVersion(packageVersion), Is.EqualTo(expected));
        }

        private static PackageInfo FindRegisteredPackage()
        {
            PackageInfo[] packages = PackageInfo.GetAllRegisteredPackages();
            if (packages == null)
            {
                return null;
            }

            for (int i = 0; i < packages.Length; i++)
            {
                PackageInfo package = packages[i];
                if (package != null && package.name == PackageId)
                {
                    return package;
                }
            }

            return null;
        }

        private static bool IsSupportedStableVersion(string packageVersion)
        {
            if (string.IsNullOrEmpty(packageVersion) ||
                packageVersion.IndexOf("-", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            int metadataSeparator = packageVersion.IndexOf("+", StringComparison.Ordinal);
            if (metadataSeparator == packageVersion.Length - 1 ||
                (metadataSeparator >= 0 &&
                 packageVersion.IndexOf("+", metadataSeparator + 1, StringComparison.Ordinal) >= 0))
            {
                return false;
            }

            string numericVersion = metadataSeparator >= 0
                ? packageVersion.Substring(0, metadataSeparator)
                : packageVersion;
            if (!Version.TryParse(numericVersion, out Version version) ||
                version.Build < 0 ||
                version.Revision >= 0)
            {
                return false;
            }

            return version.CompareTo(MinimumSupportedVersion) >= 0 &&
                   version.CompareTo(MaximumSupportedVersionExclusive) < 0;
        }

        private static bool ContainsAssembly(
            UnityEditor.Compilation.Assembly[] assemblies,
            string assemblyName)
        {
            if (assemblies == null)
            {
                return false;
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                UnityEditor.Compilation.Assembly assembly = assemblies[i];
                if (assembly != null && assembly.name == assemblyName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
