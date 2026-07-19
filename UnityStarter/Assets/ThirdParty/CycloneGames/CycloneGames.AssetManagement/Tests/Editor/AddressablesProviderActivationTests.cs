using NUnit.Framework;

using UnityEditor.Compilation;
using UnityEditor.PackageManager;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AddressablesProviderActivationTests
    {
        private const string PackageId = "com.unity.addressables";
        private const string QualifiedVersion = "2.11.1";
        private const string ProviderAssemblyName =
            "CycloneGames.AssetManagement.Runtime.Providers.Addressables";
        private const string ProviderTestAssemblyName =
            "CycloneGames.AssetManagement.Providers.Addressables.Tests.Editor";

        [Test]
        public void RegisteredPackage_ActivatesQualifiedProviderAndTests()
        {
            PackageInfo package = FindRegisteredPackage();
            if (package == null)
            {
                Assert.Pass("Addressables is optional and is not registered in this Unity project.");
                return;
            }

            Assert.That(
                package.version,
                Is.EqualTo(QualifiedVersion),
                $"Registered {PackageId} version '{package.version}' is not qualified by the AssetManagement provider. " +
                $"Install the exact supported version {QualifiedVersion} or qualify and update the provider gate.");

            UnityEditor.Compilation.Assembly[] assemblies =
                CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            Assert.That(
                ContainsAssembly(assemblies, ProviderAssemblyName),
                Is.True,
                $"{PackageId} {QualifiedVersion} is registered, but provider assembly " +
                $"'{ProviderAssemblyName}' is absent from Unity's active compilation graph.");
            Assert.That(
                ContainsAssembly(assemblies, ProviderTestAssemblyName),
                Is.True,
                $"{PackageId} {QualifiedVersion} is registered, but provider test assembly " +
                $"'{ProviderTestAssemblyName}' is absent from Unity's active compilation graph.");
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
