using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using NUnit.Framework;

namespace CycloneGames.Analyzers.Tests
{
    [TestFixture]
    public sealed class UnityActivationArtifactTests
    {
        [Test]
        public void ReleaseBuildInstallsAnalyzerAssetWithUnityActivationLabel()
        {
            string unityProjectRoot = FindUnityProjectRoot();
            string analyzerPath = Path.Combine(
                unityProjectRoot,
                "Assets",
                "Analyzers",
                "CycloneGames.Analyzers.dll");
            string metaPath = analyzerPath + ".meta";

            Assert.That(File.Exists(analyzerPath), Is.True,
                "The Release build must install the Unity-compatible analyzer DLL into Assets/Analyzers.");
            Assert.That(File.Exists(metaPath), Is.True,
                "The installed analyzer must have a tracked Unity meta file.");

            string meta = File.ReadAllText(metaPath);
            Assert.That(meta, Does.Contain("- RoslynAnalyzer"),
                "Unity only activates analyzer assets with the case-sensitive RoslynAnalyzer label.");
            Assert.That(meta, Does.Contain("Any:").And.Contain("enabled: 0"),
                "The analyzer must not be loaded as a Player or Editor runtime plugin.");
        }

        [Test]
        public void UnityArtifactHasCompatibleDependenciesAndARealCompilerFixture()
        {
            string unityProjectRoot = FindUnityProjectRoot();
            string analyzerPath = Path.Combine(
                unityProjectRoot,
                "Assets",
                "Analyzers",
                "CycloneGames.Analyzers.dll");
            string fixturePath = Path.Combine(
                unityProjectRoot,
                "Analyzers",
                "CycloneGames.Analyzers.Unity",
                "Integration",
                "ForbiddenUnityApiViolation.cs.txt");
            string verifierProjectPath = Path.Combine(
                unityProjectRoot,
                "Analyzers",
                "CycloneGames.Analyzers.Verifier",
                "CycloneGames.Analyzers.Verifier.csproj");

            Assert.That(File.Exists(fixturePath), Is.True,
                "A disabled source fixture must exercise the real Unity compiler without breaking the main project.");
            Assert.That(File.Exists(verifierProjectPath), Is.True,
                "The repository must provide a repeatable real-Unity activation verifier (the cross-platform .NET console project).");

            var references = ReadAssemblyReferences(analyzerPath);
            Assert.That(references, Does.Not.Contain("Microsoft.CodeAnalysis.Workspaces"));
            Assert.That(references, Does.Not.Contain("Microsoft.CodeAnalysis.CSharp.Workspaces"));
            Assert.That(references, Has.None.StartsWith("System.Composition"));
        }

        [Test]
        public void UnityActivationUsesAnExplicitStagedEnforcementPolicy()
        {
            string unityProjectRoot = FindUnityProjectRoot();
            string ruleSetPath = Path.Combine(
                unityProjectRoot,
                "Assets",
                "Default.ruleset");

            Assert.That(File.Exists(ruleSetPath), Is.True,
                "Unity analyzer activation must commit a visible project-wide enforcement policy.");

            XDocument ruleSet = XDocument.Load(ruleSetPath);
            Dictionary<string, string> actions = ruleSet
                .Descendants("Rule")
                .ToDictionary(
                    element => (string)element.Attribute("Id")!,
                    element => (string)element.Attribute("Action")!,
                    StringComparer.Ordinal);

            Assert.That(actions["CG0010"], Is.EqualTo("Error"));
            Assert.That(actions["CG0011"], Is.EqualTo("Warning"));
            Assert.That(actions["CG0013"], Is.EqualTo("Warning"));
        }

        [Test]
        public void CommittedAnalyzerMatchesTheUnityReleaseBuildWhenBuilt()
        {
            string unityProjectRoot = FindUnityProjectRoot();
            string committed = Path.Combine(
                unityProjectRoot,
                "Assets",
                "Analyzers",
                "CycloneGames.Analyzers.dll");
            string built = Path.Combine(
                unityProjectRoot,
                "Analyzers",
                "CycloneGames.Analyzers.Unity",
                "bin",
                "Release",
                "netstandard2.0",
                "CycloneGames.Analyzers.dll");

            if (!File.Exists(built))
            {
                Assert.Inconclusive(
                    "The Unity-compatible Release build has not been produced yet; build the solution in Release to enable this freshness check.");
            }

            byte[] committedBytes = File.ReadAllBytes(committed);
            byte[] builtBytes = File.ReadAllBytes(built);

            Assert.That(committedBytes, Is.EqualTo(builtBytes),
                "The committed analyzer DLL under Assets/Analyzers does not match the Unity Release build output. " +
                "Rebuild the solution in Release (which refreshes the committed DLL) and commit the refreshed artifact.");
        }

        private static HashSet<string> ReadAssemblyReferences(string assemblyPath)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            MetadataReader metadata = peReader.GetMetadataReader();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
            {
                AssemblyReference reference = metadata.GetAssemblyReference(handle);
                names.Add(metadata.GetString(reference.Name));
            }

            return names;
        }

        private static string FindUnityProjectRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(
                        current.FullName,
                        "ProjectSettings",
                        "ProjectVersion.txt")) &&
                    Directory.Exists(Path.Combine(current.FullName, "Assets")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate a Unity project root (a directory containing ProjectSettings/ProjectVersion.txt and Assets).");
        }
    }
}
