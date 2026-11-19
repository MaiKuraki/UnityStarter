using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CycloneGames.Localization.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CycloneGames.Localization.Tests.Editor
{
    /// <summary>
    /// Verifies the core package's dependency boundary and the serialized key fields.
    /// </summary>
    public sealed class LocalizationDecouplingGuardTests
    {
        private const string PackageId = "com.cyclone-games.localization";
        private const string PackageFolder = "CycloneGames.Localization";
        private const string BackendAssemblyPrefix = "CycloneGames.AssetManagement";
        private const string BackendPackageId = "com.cyclone-games.asset-management";
        private const string Capability = "CYCLONEGAMES_HAS_ASSETMANAGEMENT";

        private static readonly string[] CoreAsmDefs =
        {
            "Core/CycloneGames.Localization.Core.asmdef",
            "Runtime/CycloneGames.Localization.Runtime.asmdef",
            "Runtime/Components/CycloneGames.Localization.Components.asmdef",
            "Editor/CycloneGames.Localization.Editor.asmdef",
            "Tests/Editor/CycloneGames.Localization.Tests.Editor.asmdef",
        };

        private const string IntegrationAsmDef =
            "Runtime/Integrations/AssetManagement/CycloneGames.Localization.Runtime.Integrations.AssetManagement.asmdef";

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
            foreach (UnityEditor.PackageManager.PackageInfo package in
                UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (string.Equals(package.name, PackageId, StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(package.resolvedPath) &&
                    Directory.Exists(package.resolvedPath))
                {
                    return package.resolvedPath;
                }
            }

            string asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(
                "CycloneGames.Localization.Runtime");
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
                $"Localization package sources are not available in this project layout ('{PackageId}').");
            return null;
        }

        private static AsmDefSnapshot LoadAsmDef(string root, string relative)
        {
            string path = Path.Combine(root, relative);
            Assert.That(File.Exists(path), Is.True, $"Missing asmdef: {relative}");
            AsmDefSnapshot snapshot = JsonUtility.FromJson<AsmDefSnapshot>(File.ReadAllText(path));
            Assert.That(snapshot, Is.Not.Null, $"Failed to parse asmdef: {relative}");
            return snapshot;
        }

        [Test]
        public void CorePackageJson_DoesNotDeclareBackend()
        {
            string root = ResolvePackageRoot();
            string content = File.ReadAllText(Path.Combine(root, "package.json"));
            Assert.That(content.Contains(BackendPackageId), Is.False,
                "The core package must not declare an optional asset backend as a required UPM dependency.");
        }

        [Test]
        public void CoreAssemblies_ReferenceNoBackend()
        {
            string root = ResolvePackageRoot();
            var offenders = new List<string>();
            foreach (string relative in CoreAsmDefs)
            {
                AsmDefSnapshot snapshot = LoadAsmDef(root, relative);
                foreach (string reference in snapshot.references ?? Array.Empty<string>())
                {
                    if (reference != null && reference.StartsWith(BackendAssemblyPrefix, StringComparison.Ordinal))
                    {
                        offenders.Add($"{snapshot.name} -> {reference}");
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Core, Runtime, Components, Editor and core tests must compile without AssetManagement. " +
                "Offenders: " + string.Join(", ", offenders));
        }

        [Test]
        public void CoreRuntimeAssembly_HasNoCompiledBackendReference()
        {
            var offenders = new List<string>();
            foreach (AssemblyName reference in typeof(LocalizationService).Assembly.GetReferencedAssemblies())
            {
                if (reference.Name != null &&
                    reference.Name.StartsWith(BackendAssemblyPrefix, StringComparison.Ordinal))
                {
                    offenders.Add(reference.Name);
                }
            }

            Assert.That(offenders, Is.Empty,
                "The compiled core Runtime assembly must not reference AssetManagement. " +
                "Offenders: " + string.Join(", ", offenders));
        }

        [Test]
        public void CoreSources_ContainNoBackendTokens()
        {
            string root = ResolvePackageRoot();
            var offenders = new List<string>();
            foreach (string directory in new[] { "Runtime", "Editor", "Core" })
            {
                string full = Path.Combine(root, directory);
                if (!Directory.Exists(full))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (relative.StartsWith("Runtime/Integrations/AssetManagement/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string content = File.ReadAllText(file);
                    if (content.Contains(BackendAssemblyPrefix) ||
                        content.Contains("IAssetPackage") ||
                        content.Contains("IAssetHandle"))
                    {
                        offenders.Add(relative);
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Core sources must not mention backend-specific types. Offenders: " +
                string.Join(", ", offenders));
        }

        [Test]
        public void IntegrationAssembly_IsGatedAndExplicitlyComposed()
        {
            string root = ResolvePackageRoot();
            AsmDefSnapshot snapshot = LoadAsmDef(root, IntegrationAsmDef);
            Assert.That(snapshot.autoReferenced, Is.False);
            Assert.That(snapshot.defineConstraints, Is.EqualTo(new[] { Capability }));
            Assert.That(snapshot.versionDefines, Is.Not.Null);
            Assert.That(snapshot.versionDefines.Length, Is.EqualTo(1));
            Assert.That(snapshot.versionDefines[0].name, Is.EqualTo(BackendPackageId));
            Assert.That(snapshot.versionDefines[0].define, Is.EqualTo(Capability));
            Assert.That(snapshot.versionDefines[0].expression, Is.EqualTo("[1.0.0,2.0.0)"));
        }

        [Test]
        public void IntegrationSources_UseAsmdefGateOnly()
        {
            string root = ResolvePackageRoot();
            string directory = Path.GetDirectoryName(IntegrationAsmDef)?.Replace('\\', '/');
            string full = Path.Combine(root, directory);
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                foreach (string line in File.ReadLines(file))
                {
                    string directive = line.TrimStart();
                    if (directive.StartsWith("#if", StringComparison.Ordinal) &&
                        directive.Contains(Capability))
                    {
                        offenders.Add(Path.GetFileName(file));
                        break;
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Integration sources must not use a source-level #if. " +
                "Offenders: " + string.Join(", ", offenders));
        }

        [Test]
        public void AssetKey_SerializedFieldNamesAreBackwardCompatible()
        {
            foreach (Type type in new[]
            {
                typeof(LocalizedAssetRef),
                typeof(LocalizedAssetRef<UnityEngine.Object>),
            })
            {
                var names = new HashSet<string>();
                foreach (FieldInfo field in type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.GetCustomAttribute<SerializeField>() != null)
                    {
                        names.Add(field.Name);
                    }
                }

                Assert.That(names, Does.Contain("m_Location"),
                    $"{type.Name} must keep the serialized location field.");
                Assert.That(names, Does.Contain("m_GUID"),
                    $"{type.Name} must keep the serialized GUID field.");
            }
        }

        [Test]
        public void LegacyAssetTableFixture_KeepsLocationAndGuid()
        {
            AssetTable table = ScriptableObject.CreateInstance<AssetTable>();
            try
            {
                var serialized = new SerializedObject(table);
                serialized.FindProperty("tableId").stringValue = "fixture";
                serialized.FindProperty("localeCode").stringValue = "en";
                SerializedProperty entries = serialized.FindProperty("entries");
                Assert.That(entries, Is.Not.Null, "AssetTable.entries must remain serializable.");
                entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("Key").stringValue = "flag";

                SerializedProperty asset = entry.FindPropertyRelative("Asset");
                Assert.That(asset, Is.Not.Null, "AssetEntry.Asset must remain serializable.");
                SerializedProperty location = asset.FindPropertyRelative("m_Location");
                SerializedProperty guid = asset.FindPropertyRelative("m_GUID");
                Assert.That(location, Is.Not.Null,
                    "LocalizationCatalogBuilder reads m_Location; the path must remain valid.");
                Assert.That(guid, Is.Not.Null,
                    "LocalizationCatalogBuilder reads m_GUID; the path must remain valid.");

                location.stringValue = "Assets/Flag_en.png";
                guid.stringValue = "flag-guid";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(table.TryGetValue("flag", out LocalizedAssetRef value), Is.True);
                Assert.That(value.Location, Is.EqualTo("Assets/Flag_en.png"));
                Assert.That(value.Guid, Is.EqualTo("flag-guid"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(table);
            }
        }
    }
}
