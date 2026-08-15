using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace CycloneGames.Analyzers.Tests
{
    [TestFixture]
    public sealed class AnalyzerSourceOwnershipTests
    {
        private const string Source = """
            namespace UnityEngine
            {
                public sealed class GameObject
                {
                    public static GameObject Find(string name) => null;
                }
            }

            public static class Consumer
            {
                public static UnityEngine.GameObject FindTarget()
                    => UnityEngine.GameObject.Find("Target");
            }
            """;

        private string _fixtureRoot = null!;
        private string _projectRoot = null!;
        private string _reverseDnsAncestorProjectRoot = null!;

        [OneTimeSetUp]
        public void CreatePhysicalUnityProjectMarkers()
        {
            _fixtureRoot = Path.Combine(
                Path.GetTempPath(),
                "CycloneGamesAnalyzerSourceScope-" + Guid.NewGuid().ToString("N"));
            _projectRoot = Path.Combine(_fixtureRoot, "StandardProject");
            _reverseDnsAncestorProjectRoot = Path.Combine(
                _fixtureRoot,
                "Packages",
                "com.company.product",
                "repository",
                "UnityProject");

            CreateUnityProjectMarker(_projectRoot);
            CreateUnityProjectMarker(_reverseDnsAncestorProjectRoot);
        }

        [OneTimeTearDown]
        public void DeletePhysicalUnityProjectMarkers()
        {
            if (!string.IsNullOrEmpty(_fixtureRoot) && Directory.Exists(_fixtureRoot))
            {
                Directory.Delete(_fixtureRoot, recursive: true);
            }
        }

        [TestCase("Library/PackageCache/com.unity.test-framework/Runtime/Consumer.cs")]
        [TestCase("Packages/com.vendor.package/Runtime/Consumer.cs")]
        [TestCase("Library/PackageCache/com.vendor/Assets/Consumer.cs")]
        [TestCase("Packages/com.vendor/Assets/Consumer.cs")]
        [TestCase("Assets/ThirdParty/Vendor/Runtime/Consumer.cs")]
        [TestCase("Temp/Generated/Consumer.g.cs")]
        [TestCase("External/Runtime/Consumer.cs")]
        public async Task DoesNotReportDiagnosticsForExternalSource(string pathBelowFixture)
        {
            string sourcePath = Path.Combine(
                _projectRoot,
                pathBelowFixture.Replace('/', Path.DirectorySeparatorChar));

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(sourcePath);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public async Task DoesNotTrustAnAssetsDirectoryWithoutARegularProjectMarker()
        {
            string sourcePath = Path.Combine(
                _fixtureRoot,
                "NotAUnityProject",
                "Assets",
                "Build",
                "Runtime",
                "Consumer.cs");

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(sourcePath);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public async Task DoesNotTrustADirectoryAtTheProjectVersionMarkerPath()
        {
            string invalidRoot = Path.Combine(_fixtureRoot, "DirectoryMarkerProject");
            Directory.CreateDirectory(Path.Combine(
                invalidRoot,
                "ProjectSettings",
                "ProjectVersion.txt"));
            string sourcePath = Path.Combine(
                invalidRoot,
                "Assets",
                "Build",
                "Runtime",
                "Consumer.cs");

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(sourcePath);

            Assert.That(diagnostics, Is.Empty);
        }

        [TestCase("Assets/Game/Runtime/Consumer.cs")]
        [TestCase("Assets/Build/Runtime/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames/Feature/Runtime/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames.MemoryGovernance/Main/Runtime/Consumer.cs")]
        public async Task ReportsDiagnosticsForVerifiedRepositorySource(string projectRelativePath)
        {
            string sourcePath = Path.Combine(
                _projectRoot,
                projectRelativePath.Replace('/', Path.DirectorySeparatorChar));

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(sourcePath);

            Assert.That(diagnostics.Select(diagnostic => diagnostic.Id),
                Is.EqualTo(new[] { DiagnosticIds.GameObjectFind }));
        }

        [Test]
        public async Task ReportsForARealProjectBelowAReverseDnsPackagesAncestor()
        {
            string sourcePath = Path.Combine(
                _reverseDnsAncestorProjectRoot,
                "Assets",
                "Build",
                "Runtime",
                "Consumer.cs");

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(sourcePath);

            Assert.That(diagnostics.Select(diagnostic => diagnostic.Id),
                Is.EqualTo(new[] { DiagnosticIds.GameObjectFind }));
        }

        [TestCase("Assets/Build/Runtime/Consumer.cs", true)]
        [TestCase("Assets/ThirdParty/CycloneGames/Runtime/Consumer.cs", true)]
        [TestCase("Assets/ThirdParty/Vendor/Runtime/Consumer.cs", false)]
        [TestCase("Packages/com.vendor/Assets/Consumer.cs", false)]
        [TestCase("Library/PackageCache/com.vendor/Assets/Consumer.cs", false)]
        [TestCase("Temp/Generated/Consumer.g.cs", false)]
        [TestCase("Consumer.cs", false)]
        [TestCase("./Assets/Build/Consumer.cs", false)]
        [TestCase("Assets/../Packages/com.vendor/Consumer.cs", false)]
        public void AppliesExplicitFailClosedRelativePathPolicy(
            string sourcePath,
            bool expected)
        {
            Assert.That(
                AnalyzerSourceScope.IsRepositoryOwned(sourcePath, _projectRoot),
                Is.EqualTo(expected));
        }

        [Test]
        public void RejectsRelativeAssetsWhenTheBaseDirectoryIsNotAVerifiedUnityRoot()
        {
            Assert.That(
                AnalyzerSourceScope.IsRepositoryOwned(
                    "Assets/Build/Runtime/Consumer.cs",
                    Path.Combine(_fixtureRoot, "NotAUnityProject")),
                Is.False);
        }

        [TestCase("Assets/Build/Runtime/Consumer.cs", StringComparison.Ordinal, true)]
        [TestCase("assets/Build/Runtime/Consumer.cs", StringComparison.Ordinal, false)]
        [TestCase("assets/Build/Runtime/Consumer.cs", StringComparison.OrdinalIgnoreCase, true)]
        public void RelativeAssetRootUsesTheHostFilesystemComparison(
            string sourcePath,
            StringComparison pathComparison,
            bool expected)
        {
            Assert.That(
                AnalyzerSourceScope.IsCanonicalRelativeAssetPath(
                    sourcePath,
                    pathComparison),
                Is.EqualTo(expected));
        }

        [TestCase("/Assets/ThirdParty/CycloneGames/Runtime/Consumer.cs/", StringComparison.Ordinal, true)]
        [TestCase("/Assets/ThirdParty/cyclonegames/Runtime/Consumer.cs/", StringComparison.Ordinal, false)]
        [TestCase("/Assets/ThirdParty/cyclonegames/Runtime/Consumer.cs/", StringComparison.OrdinalIgnoreCase, true)]
        [TestCase("/Assets/thirdparty/Vendor/Runtime/Consumer.cs/", StringComparison.Ordinal, true)]
        [TestCase("/Assets/thirdparty/Vendor/Runtime/Consumer.cs/", StringComparison.OrdinalIgnoreCase, false)]
        [TestCase("/assets/Build/Runtime/Consumer.cs/", StringComparison.Ordinal, false)]
        public void AssetAllowlistUsesTheHostFilesystemComparison(
            string normalizedAssetPath,
            StringComparison pathComparison,
            bool expected)
        {
            Assert.That(
                AnalyzerSourceScope.IsOwnedAssetPath(
                    normalizedAssetPath,
                    pathComparison),
                Is.EqualTo(expected));
        }

        [TestCase("/repo/Assets/Build/Consumer.cs", StringComparison.Ordinal, 5)]
        [TestCase("/repo/assets/Build/Consumer.cs", StringComparison.Ordinal, -1)]
        [TestCase("/repo/assets/Build/Consumer.cs", StringComparison.OrdinalIgnoreCase, 5)]
        [TestCase("/repo/Assets/Outer/Assets/Build/Consumer.cs", StringComparison.Ordinal, 18)]
        public void CandidateAssetsRootUsesTheHostFilesystemComparison(
            string normalizedFullPath,
            StringComparison pathComparison,
            int expectedIndex)
        {
            Assert.That(
                AnalyzerSourceScope.FindAssetsRootIndex(
                    normalizedFullPath,
                    pathComparison),
                Is.EqualTo(expectedIndex));
        }

        [Test]
        public void KeepsOnlyTheEmptyRoslynTestPathInScope()
        {
            Assert.That(AnalyzerSourceScope.IsRepositoryOwned(string.Empty), Is.True);
            Assert.That(AnalyzerSourceScope.IsRepositoryOwned((string?)null), Is.True);
        }

        [TestCase("Assets/Build/Runtime/Consumer.cs", true)]
        [TestCase("Packages/com.vendor.package/Runtime/Consumer.cs", false)]
        public void ReturnsStableOwnershipForTheSameTreeDuringConcurrentAccess(
            string sourcePath,
            bool expected)
        {
            if (sourcePath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                sourcePath = Path.Combine(
                    _projectRoot,
                    sourcePath.Replace('/', Path.DirectorySeparatorChar));
            }

            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                Source,
                new CSharpParseOptions(LanguageVersion.CSharp9),
                sourcePath);
            var results = new bool[256];

            Parallel.For(
                0,
                results.Length,
                index => results[index] = AnalyzerSourceScope.IsRepositoryOwned(tree));

            Assert.That(results, Is.All.EqualTo(expected));
        }

        private static void CreateUnityProjectMarker(string projectRoot)
        {
            string projectSettings = Path.Combine(projectRoot, "ProjectSettings");
            Directory.CreateDirectory(projectSettings);
            File.WriteAllText(
                Path.Combine(projectSettings, "ProjectVersion.txt"),
                "m_EditorVersion: 2022.3.62f3\n");
        }

        private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string sourcePath)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                Source,
                new CSharpParseOptions(LanguageVersion.CSharp9),
                sourcePath);
            var compilation = CSharpCompilation.Create(
                "Repository.SourceOwnership",
                new[] { tree },
                LoadPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return await compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(
                    new ForbiddenUnityApiAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();
        }

        private static ImmutableArray<MetadataReference> LoadPlatformReferences()
        {
            string? trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrEmpty(trustedAssemblies))
            {
                throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
            }

            return trustedAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToImmutableArray<MetadataReference>();
        }
    }
}
