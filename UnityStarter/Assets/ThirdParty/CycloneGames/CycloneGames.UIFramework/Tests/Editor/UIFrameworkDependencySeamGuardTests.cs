using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    /// <summary>
    /// Guards the optional-backend seam: core references, integration gates and
    /// serialized key fields.
    /// </summary>
    public sealed class UIFrameworkDependencySeamGuardTests
    {
        private const string PackageId = "com.cyclone-games.ui-framework";
        private const string PackageFolder = "CycloneGames.UIFramework";
        private const string OptionalBackend = "CycloneGames.AssetManagement";
        private const string OptionalPackageId = "com.cyclone-games.asset-management";
        private const string CapabilityDefine = "CYCLONEGAMES_HAS_ASSETMANAGEMENT";

        private static readonly string[] CoreAsmDefs =
        {
            "Runtime/Scripts/CycloneGames.UIFramework.Runtime.asmdef",
            "Editor/CycloneGames.UIFramework.Editor.asmdef",
            "Tests/Editor/CycloneGames.UIFramework.Tests.Editor.asmdef",
            "Samples/CycloneGames.UIFramework.Samples.asmdef",
        };

        private static readonly string[] IntegrationAsmDefs =
        {
            "Runtime/Scripts/Integrations/AssetManagement/CycloneGames.UIFramework.Runtime.Integrations.AssetManagement.asmdef",
            "Editor/Integrations/AssetManagement/CycloneGames.UIFramework.Editor.Integrations.AssetManagement.asmdef",
            "Tests/Editor/Integrations/AssetManagement/CycloneGames.UIFramework.Tests.Editor.Integrations.AssetManagement.asmdef",
        };

        [Serializable]
        private sealed class AsmDefSnapshot
        {
            public string name;
            public string[] references;
            public string[] defineConstraints;
            public bool autoReferenced;
            public VersionDefineSnapshot[] versionDefines;
        }

        [Serializable]
        private sealed class VersionDefineSnapshot
        {
            public string name;
            public string expression;
            public string define;
        }

        private static string ResolvePackageRoot()
        {
            foreach (PackageInfo package in PackageInfo.GetAllRegisteredPackages())
            {
                if (string.Equals(package.name, PackageId, StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(package.resolvedPath) &&
                    Directory.Exists(package.resolvedPath))
                {
                    return package.resolvedPath;
                }
            }

            string asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(
                "CycloneGames.UIFramework.Runtime");
            if (!string.IsNullOrEmpty(asmdefPath))
            {
                string directory = Path.GetDirectoryName(asmdefPath)?.Replace('\\', '/');
                while (!string.IsNullOrEmpty(directory))
                {
                    if (string.Equals(Path.GetFileName(directory), PackageFolder, StringComparison.Ordinal))
                    {
                        return Path.GetFullPath(directory);
                    }

                    string parent = Path.GetDirectoryName(directory);
                    if (string.Equals(parent, directory, StringComparison.Ordinal))
                    {
                        break;
                    }

                    directory = parent;
                }
            }

            Assert.Ignore(
                $"UIFramework package sources are not available in this project layout ('{PackageId}').");
            return null;
        }

        private static AsmDefSnapshot LoadAsmDef(string root, string relativePath)
        {
            string path = Path.Combine(root, relativePath);
            Assert.That(File.Exists(path), Is.True, $"Missing asmdef: {relativePath}");
            AsmDefSnapshot snapshot = JsonUtility.FromJson<AsmDefSnapshot>(File.ReadAllText(path));
            Assert.That(snapshot, Is.Not.Null, $"Failed to parse asmdef: {relativePath}");
            return snapshot;
        }

        [Test]
        public void CorePackageJson_DoesNotDeclareOptionalBackend()
        {
            string root = ResolvePackageRoot();
            string path = Path.Combine(root, "package.json");
            Assert.That(File.Exists(path), Is.True, "Missing package.json.");
            string content = File.ReadAllText(path);
            Assert.That(content.Contains(OptionalPackageId), Is.False,
                "The core package must not declare an optional asset backend as a required UPM " +
                "dependency; UPM would install it unconditionally and defeat the integration gate.");
        }

        [Test]
        public void CoreAssemblies_ReferenceNoOptionalBackend()
        {
            string root = ResolvePackageRoot();
            var offenders = new List<string>();
            foreach (string relative in CoreAsmDefs)
            {
                AsmDefSnapshot snapshot = LoadAsmDef(root, relative);
                if (snapshot.references == null)
                {
                    continue;
                }

                foreach (string reference in snapshot.references)
                {
                    if (reference != null &&
                        reference.StartsWith(OptionalBackend, StringComparison.Ordinal))
                    {
                        offenders.Add($"{snapshot.name} -> {reference}");
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Core, Editor, core tests and samples must compile with AssetManagement absent. " +
                "Offenders: " + string.Join(", ", offenders));
        }

        [Test]
        public void CoreRuntimeAssembly_HasNoCompiledBackendReference()
        {
            var offenders = new List<string>();
            foreach (AssemblyName reference in
                typeof(IUIWindowAssetProvider).Assembly.GetReferencedAssemblies())
            {
                if (reference.Name != null &&
                    reference.Name.StartsWith(OptionalBackend, StringComparison.Ordinal))
                {
                    offenders.Add(reference.Name);
                }
            }

            Assert.That(offenders, Is.Empty,
                "The compiled core Runtime assembly must not reference AssetManagement. " +
                "Offenders: " + string.Join(", ", offenders));
        }

        [Test]
        public void IntegrationAssemblies_DoNotAutoReference()
        {
            string root = ResolvePackageRoot();
            var offenders = new List<string>();
            foreach (string relative in IntegrationAsmDefs)
            {
                AsmDefSnapshot snapshot = LoadAsmDef(root, relative);
                if (snapshot.autoReferenced)
                {
                    offenders.Add(snapshot.name);
                }
            }

            Assert.That(offenders, Is.Empty,
                "Integration assemblies set autoReferenced:false so predefined assemblies never " +
                "reference them implicitly. Runtime adapters additionally require an explicit host " +
                "composition reference, while the Editor resolver is discovered through TypeCache and " +
                "activates with the backend package. Offenders: " +
                string.Join(", ", offenders));
        }

        [Test]
        public void IntegrationSources_UseAsmdefGateOnly()
        {
            string root = ResolvePackageRoot();
            var offenders = new List<string>();
            foreach (string relative in IntegrationAsmDefs)
            {
                string directory = Path.GetDirectoryName(relative)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(directory))
                {
                    continue;
                }

                string full = Path.Combine(root, directory);
                if (!Directory.Exists(full))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories))
                {
                    foreach (string line in File.ReadLines(file))
                    {
                        string directive = line.TrimStart();
                        if (directive.StartsWith("#if", StringComparison.Ordinal) &&
                            directive.Contains(CapabilityDefine))
                        {
                            offenders.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
                            break;
                        }
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Integration sources must not use a source-level #if on the capability symbol. " +
                "Offenders: " +
                string.Join(", ", offenders));
        }

        [Test]
        public void EditorResolver_IsDiscoveredNotReferenced()
        {
            const string editorIntegration =
                "CycloneGames.UIFramework.Editor.Integrations.AssetManagement";
            string root = ResolvePackageRoot();
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories))
            {
                AsmDefSnapshot snapshot = JsonUtility.FromJson<AsmDefSnapshot>(File.ReadAllText(file));
                if (snapshot == null || snapshot.name == editorIntegration || snapshot.references == null)
                {
                    continue;
                }

                foreach (string reference in snapshot.references)
                {
                    if (reference == editorIntegration)
                    {
                        offenders.Add(snapshot.name);
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "The Editor location resolver is discovered through TypeCache, not through an " +
                "assembly reference. A direct reference would make its implicit activation " +
                "ambiguous. Offenders: " + string.Join(", ", offenders));
        }

        [Test]
        public void IntegrationAssemblies_ShareOneGateDefinition()
        {
            string root = ResolvePackageRoot();
            var signatures = new List<string>();
            foreach (string relative in IntegrationAsmDefs)
            {
                AsmDefSnapshot snapshot = LoadAsmDef(root, relative);
                Assert.That(snapshot.defineConstraints, Is.Not.Null,
                    $"{snapshot.name} must declare define constraints.");
                Assert.That(snapshot.defineConstraints.Length, Is.EqualTo(1),
                    $"{snapshot.name} must declare exactly one define constraint.");
                Assert.That(snapshot.defineConstraints[0], Is.EqualTo(CapabilityDefine),
                    $"{snapshot.name} uses an unexpected capability symbol.");
                Assert.That(snapshot.versionDefines, Is.Not.Null,
                    $"{snapshot.name} must declare a version define.");
                Assert.That(snapshot.versionDefines.Length, Is.EqualTo(1),
                    $"{snapshot.name} must declare exactly one version define.");

                VersionDefineSnapshot gate = snapshot.versionDefines[0];
                Assert.That(gate.name, Is.EqualTo(OptionalPackageId));
                Assert.That(gate.define, Is.EqualTo(CapabilityDefine));
                signatures.Add($"{gate.name}|{gate.expression}|{gate.define}");
            }

            for (int i = 1; i < signatures.Count; i++)
            {
                Assert.That(signatures[i], Is.EqualTo(signatures[0]),
                    "All integration assemblies must repeat the identical versionDefines rule; " +
                    "version-defined symbols do not propagate between assemblies.");
            }
        }

        [Test]
        public void CoreRuntimeSources_ContainNoBackendTokens()
        {
            string root = ResolvePackageRoot();
            string runtimeRoot = Path.Combine(root, "Runtime/Scripts");
            Assert.That(Directory.Exists(runtimeRoot), Is.True, "Missing Runtime/Scripts directory.");

            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(runtimeRoot, file).Replace('\\', '/');
                if (relative.StartsWith("Integrations/AssetManagement/", StringComparison.Ordinal))
                {
                    continue;
                }

                string content = File.ReadAllText(file);
                if (content.Contains(OptionalBackend) ||
                    content.Contains("IAssetHandle") ||
                    content.Contains("IAssetPackage") ||
                    content.Contains("AssetBucketScope"))
                {
                    offenders.Add(relative);
                }
            }

            Assert.That(offenders, Is.Empty,
                "Core runtime sources must not mention backend-specific types. Offenders: " +
                string.Join(", ", offenders));
        }
    }
}
