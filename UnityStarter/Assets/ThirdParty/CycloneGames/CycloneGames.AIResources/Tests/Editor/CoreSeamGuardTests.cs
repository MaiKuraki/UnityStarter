using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace CycloneGames.AIResources.Tests.Editor
{
    /// <summary>
    /// Static guard for the engine-free Core assemblies. It reads sources and assembly metadata
    /// only: no scene, no play mode, no load. A failure here means the Core seam has been broken,
    /// not that a behaviour regressed.
    /// </summary>
    public sealed class CoreSeamGuardTests
    {
        private static readonly string[] CoreAssemblies =
        {
            "CycloneGames.BehaviorTree.Core",
            "CycloneGames.AIResources.Core",
            "CycloneGames.AIEnvQuery.Core"
        };

        private static readonly Regex HostSymbol = new Regex(
            @"^\s*#(?:if|elif)\b.*\b(UNITY_[A-Z_]+|GODOT[A-Z_]*|UNREAL[A-Z_]*|UE_[A-Z_]+)",
            RegexOptions.Compiled);

        private static readonly Regex UnityUsage = new Regex(
            @"\bUnityEngine\b|\bUnityEditor\b",
            RegexOptions.Compiled);

        private static readonly Regex DiskIo = new Regex(
            @"\bFile\.(?:Open|Read|Write|Create|Delete|Exists|Copy|Move)\b|\bFileStream\b|\bDirectory\.(?:Create|Delete|Exists|Enumerate)",
            RegexOptions.Compiled);

        [Test]
        public void CoreAssemblies_AreMarkedEngineFree()
        {
            foreach (string assemblyName in CoreAssemblies)
            {
                string asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assemblyName);
                Assert.IsFalse(string.IsNullOrEmpty(asmdefPath), $"asmdef for '{assemblyName}' was not found.");

                string json = File.ReadAllText(asmdefPath);
                StringAssert.IsMatch(
                    "\"noEngineReferences\"\\s*:\\s*true",
                    json,
                    $"{assemblyName} must keep noEngineReferences enabled.");
            }
        }

        [Test]
        public void CoreSources_UseNoHostSpecificCompilationSymbols()
        {
            foreach (string assemblyName in CoreAssemblies)
            {
                string directory = ResolveSourceDirectory(assemblyName);
                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    int lineNumber = 0;
                    foreach (string line in File.ReadLines(file))
                    {
                        lineNumber++;
                        Match match = HostSymbol.Match(line);
                        Assert.IsFalse(
                            match.Success,
                            $"{file}:{lineNumber} gates on the host symbol '{match.Groups[1].Value}'. Core must depend on DEBUG or CycloneGames symbols only.");
                    }
                }
            }
        }

        [Test]
        public void CoreSources_DoNotTouchUnityOrDisk()
        {
            foreach (string assemblyName in CoreAssemblies)
            {
                string directory = ResolveSourceDirectory(assemblyName);
                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    int lineNumber = 0;
                    foreach (string rawLine in File.ReadLines(file))
                    {
                        lineNumber++;
                        string line = StripLineComment(rawLine);
                        Assert.IsFalse(
                            UnityUsage.IsMatch(line),
                            $"{file}:{lineNumber} references a Unity type inside an engine-free assembly.");
                        Assert.IsFalse(
                            DiskIo.IsMatch(line),
                            $"{file}:{lineNumber} performs disk IO inside an engine-free assembly.");
                    }
                }
            }
        }

        [Test]
        public void CoreAssemblies_ReferenceNoUnityAssembly()
        {
            foreach (string assemblyName in CoreAssemblies)
            {
                System.Reflection.Assembly assembly = System.Reflection.Assembly.Load(assemblyName);
                Assert.IsNotNull(assembly, $"'{assemblyName}' did not load.");

                foreach (System.Reflection.AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    Assert.IsFalse(
                        reference.Name != null && reference.Name.StartsWith("Unity", StringComparison.Ordinal),
                        $"{assemblyName} references '{reference.Name}'. The Core seam must stay engine-free.");
                }
            }
        }

        private static string ResolveSourceDirectory(string assemblyName)
        {
            // The asmdef sits inside the Core folder, so its directory is the source root.
            string asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assemblyName);
            Assert.IsFalse(string.IsNullOrEmpty(asmdefPath), $"asmdef for '{assemblyName}' was not found.");

            string directory = Path.GetDirectoryName(asmdefPath);
            Assert.IsNotNull(directory);
            Assert.IsTrue(Directory.Exists(directory), $"source directory for '{assemblyName}' does not exist.");
            return directory;
        }

        private static string StripLineComment(string line)
        {
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            return comment >= 0 ? line.Substring(0, comment) : line;
        }
    }
}
