using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CycloneGames.Logging.Tests
{
    public sealed class PureCoreDependencyTests
    {
        private const string LoggingAssembly = "CycloneGames.Logging.Core";
        private const string LoggingPipelineAssemblyPrefix = "CycloneGames.Logging.Pipeline";
        private const string LoggingUnityAssemblyPrefix = "CycloneGames.Logging.Unity";
        private const string LoggingIntegrationSuffix = ".Integrations.Logging";

        private static readonly Regex NamePattern = new Regex(
            "\\\"name\\\"\\s*:\\s*\\\"(?<value>[^\\\"]+)\\\"",
            RegexOptions.CultureInvariant);

        private static readonly Regex ReferencesPattern = new Regex(
            "\\\"references\\\"\\s*:\\s*\\[(?<value>.*?)\\]",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);

        private static readonly Regex JsonStringPattern = new Regex(
            "\\\"(?<value>[^\\\"]+)\\\"",
            RegexOptions.CultureInvariant);

        private static readonly Regex DirectLoggingPattern = new Regex(
            @"\b(?:UnityEngine\s*\.\s*)?Debug\s*\.\s*(?:Log[A-Za-z0-9_]*|Assert[A-Za-z0-9_]*|unityLogger)\b|" +
            @"\b(?:System\s*\.\s*)?Console\s*\.\s*(?:Write[A-Za-z0-9_]*|Out|Error)\b|" +
            @"\bprint\s*\(|\bLogPipeline\b",
            RegexOptions.CultureInvariant);

        private static readonly Lazy<ArchitectureSnapshot> Snapshot =
            new Lazy<ArchitectureSnapshot>(ArchitectureSnapshot.Load);

        [Test]
        public void StrictPureCoreProfile_HasNoEngineOrLoggingDependencyPath()
        {
            IReadOnlyList<AssemblyDefinition> strictCores = Snapshot.Value.Assemblies
                .Where(IsStrictPureCore)
                .OrderBy(definition => definition.Name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(strictCores, Is.Not.Empty, "No strict PureCore asmdef was discovered.");

            var violations = new List<string>();
            for (int i = 0; i < strictCores.Count; i++)
            {
                FindForbiddenDependencyPaths(strictCores[i], violations);
            }

            Assert.That(
                violations,
                Is.Empty,
                "Strict PureCore assemblies are production '*.Core' asmdefs with " +
                "noEngineReferences=true. Forbidden dependency paths:\n" +
                string.Join("\n", violations));
        }

        [Test]
        public void LoggingIntegrations_AreOptionalOneWayAdapters()
        {
            AssemblyDefinition[] integrations = Snapshot.Value.Assemblies
                .Where(definition =>
                    !definition.IsExempt &&
                    definition.Name.EndsWith(LoggingIntegrationSuffix, StringComparison.Ordinal))
                .OrderBy(definition => definition.Name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(integrations, Is.Not.Empty, "No strict PureCore Logging integration was discovered.");

            var violations = new List<string>();
            for (int i = 0; i < integrations.Length; i++)
            {
                AssemblyDefinition integration = integrations[i];
                string ownerName = integration.Name.Substring(
                    0,
                    integration.Name.Length - LoggingIntegrationSuffix.Length);
                string expectedCore = ownerName + ".Core";
                string[] references = ResolveReferences(integration).ToArray();

                if (integration.AutoReferenced)
                {
                    violations.Add(integration.Name + " must set autoReferenced=false.");
                }

                if (!integration.NoEngineReferences)
                {
                    violations.Add(integration.Name + " must set noEngineReferences=true.");
                }

                if (!references.Contains(LoggingAssembly, StringComparer.Ordinal))
                {
                    violations.Add(integration.Name + " must reference " + LoggingAssembly + ".");
                }

                if (!references.Contains(expectedCore, StringComparer.Ordinal))
                {
                    violations.Add(integration.Name + " must reference its owner " + expectedCore + ".");
                }

                string[] forbidden = references.Where(IsForbiddenIntegrationReference).ToArray();
                if (forbidden.Length != 0)
                {
                    violations.Add(integration.Name + " has forbidden references: " + string.Join(", ", forbidden));
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Logging integrations must point from an optional adapter to the owner Core + CycloneGames.Logging.Core, never back into Core:\n" +
                string.Join("\n", violations));
        }

        [Test]
        public void GovernedProductionSources_DoNotBypassLoggingContract()
        {
            var violations = new List<string>();
            string root = Snapshot.Value.CycloneGamesRoot;

            foreach (string sourcePath in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (IsExemptSourcePath(sourcePath, root))
                {
                    continue;
                }

                string source = ReadAllText(sourcePath);
                string codeOnly = MaskCommentsAndLiterals(source);
                MatchCollection matches = DirectLoggingPattern.Matches(codeOnly);
                for (int i = 0; i < matches.Count; i++)
                {
                    Match match = matches[i];
                    violations.Add(
                        MakeRelativePath(root, sourcePath) + ":" +
                        GetLineNumber(codeOnly, match.Index) + " uses " + match.Value.Trim() + ".");
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Production CycloneGames sources outside Logging backend packages must use the shared contract. " +
                "Tests, tools, code generation, and generated files are excluded:\n" +
                string.Join("\n", violations));
        }

        private static bool IsStrictPureCore(AssemblyDefinition definition)
        {
            return !definition.IsExempt &&
                   definition.NoEngineReferences &&
                   definition.Name.EndsWith(".Core", StringComparison.Ordinal);
        }

        private static void FindForbiddenDependencyPaths(
            AssemblyDefinition root,
            ICollection<string> violations)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var path = new List<string> { root.Name };
            Visit(root, visited, path, violations);
        }

        private static void Visit(
            AssemblyDefinition current,
            ISet<string> visited,
            IList<string> path,
            ICollection<string> violations)
        {
            if (!visited.Add(current.Name))
            {
                return;
            }

            foreach (string reference in ResolveReferences(current))
            {
                path.Add(reference);

                if (IsForbiddenPureCoreReference(reference))
                {
                    violations.Add(string.Join(" -> ", path));
                    path.RemoveAt(path.Count - 1);
                    continue;
                }

                if (Snapshot.Value.ByName.TryGetValue(reference, out AssemblyDefinition referencedDefinition))
                {
                    if (!referencedDefinition.NoEngineReferences)
                    {
                        violations.Add(string.Join(" -> ", path) + " [noEngineReferences=false]");
                    }
                    else
                    {
                        Visit(referencedDefinition, visited, path, violations);
                    }
                }

                path.RemoveAt(path.Count - 1);
            }
        }

        private static IEnumerable<string> ResolveReferences(AssemblyDefinition definition)
        {
            for (int i = 0; i < definition.References.Length; i++)
            {
                string reference = definition.References[i];
                if (reference.StartsWith("GUID:", StringComparison.Ordinal))
                {
                    string guid = reference.Substring("GUID:".Length);
                    if (Snapshot.Value.NameByGuid.TryGetValue(guid, out string resolvedName))
                    {
                        yield return resolvedName;
                    }
                    else
                    {
                        yield return reference;
                    }
                }
                else
                {
                    yield return reference;
                }
            }
        }

        private static bool IsForbiddenPureCoreReference(string assemblyName)
        {
            return assemblyName.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                   assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal) ||
                   string.Equals(assemblyName, LoggingAssembly, StringComparison.Ordinal) ||
                   IsLoggingBackendAssembly(assemblyName) ||
                   assemblyName.EndsWith(LoggingIntegrationSuffix, StringComparison.Ordinal);
        }

        private static bool IsForbiddenIntegrationReference(string assemblyName)
        {
            return assemblyName.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                   assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal) ||
                   IsLoggingBackendAssembly(assemblyName) ||
                   assemblyName.EndsWith(LoggingIntegrationSuffix, StringComparison.Ordinal);
        }

        private static bool IsLoggingBackendAssembly(string assemblyName)
        {
            return string.Equals(assemblyName, LoggingPipelineAssemblyPrefix, StringComparison.Ordinal) ||
                   assemblyName.StartsWith(LoggingPipelineAssemblyPrefix + ".", StringComparison.Ordinal) ||
                   string.Equals(assemblyName, LoggingUnityAssemblyPrefix, StringComparison.Ordinal) ||
                   assemblyName.StartsWith(LoggingUnityAssemblyPrefix + ".", StringComparison.Ordinal);
        }

        private static bool IsExemptSourcePath(string sourcePath, string root)
        {
            string relative = MakeRelativePath(root, sourcePath);
            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (string.Equals(segments[i], "CycloneGames.Logging.Pipeline", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segments[i], "CycloneGames.Logging.Unity", StringComparison.OrdinalIgnoreCase) ||
                    IsExemptSegment(segments[i]))
                {
                    return true;
                }
            }

            string fileName = Path.GetFileName(sourcePath);
            return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExemptSegment(string segment)
        {
            return string.Equals(segment, "Test", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "Test~", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "Tests", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "Tests~", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "Tool", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "Tool~", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "Tools", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "Tools~", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "CodeGen", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "CodeGen~", StringComparison.OrdinalIgnoreCase);
        }

        private static string MaskCommentsAndLiterals(string source)
        {
            char[] result = source.ToCharArray();
            int index = 0;

            while (index < result.Length)
            {
                if (result[index] == '/' && index + 1 < result.Length && result[index + 1] == '/')
                {
                    Mask(result, index++);
                    Mask(result, index++);
                    while (index < result.Length && result[index] != '\r' && result[index] != '\n')
                    {
                        Mask(result, index++);
                    }

                    continue;
                }

                if (result[index] == '/' && index + 1 < result.Length && result[index + 1] == '*')
                {
                    Mask(result, index++);
                    Mask(result, index++);
                    while (index < result.Length)
                    {
                        if (result[index] == '*' && index + 1 < result.Length && result[index + 1] == '/')
                        {
                            Mask(result, index++);
                            Mask(result, index++);
                            break;
                        }

                        Mask(result, index++);
                    }

                    continue;
                }

                if (TryMaskString(result, ref index) || TryMaskCharacter(result, ref index))
                {
                    continue;
                }

                index++;
            }

            return new string(result);
        }

        private static bool TryMaskString(char[] source, ref int index)
        {
            bool verbatim = false;
            int prefixLength;

            if (source[index] == '"')
            {
                prefixLength = 1;
            }
            else if (source[index] == '$' && index + 1 < source.Length && source[index + 1] == '"')
            {
                prefixLength = 2;
            }
            else if (source[index] == '@' && index + 1 < source.Length && source[index + 1] == '"')
            {
                prefixLength = 2;
                verbatim = true;
            }
            else if (index + 2 < source.Length &&
                     ((source[index] == '$' && source[index + 1] == '@') ||
                      (source[index] == '@' && source[index + 1] == '$')) &&
                     source[index + 2] == '"')
            {
                prefixLength = 3;
                verbatim = true;
            }
            else
            {
                return false;
            }

            for (int i = 0; i < prefixLength; i++)
            {
                Mask(source, index++);
            }

            while (index < source.Length)
            {
                if (!verbatim && source[index] == '\\' && index + 1 < source.Length)
                {
                    Mask(source, index++);
                    Mask(source, index++);
                    continue;
                }

                if (source[index] == '"')
                {
                    Mask(source, index++);
                    if (verbatim && index < source.Length && source[index] == '"')
                    {
                        Mask(source, index++);
                        continue;
                    }

                    break;
                }

                Mask(source, index++);
            }

            return true;
        }

        private static bool TryMaskCharacter(char[] source, ref int index)
        {
            if (source[index] != '\'')
            {
                return false;
            }

            Mask(source, index++);
            while (index < source.Length)
            {
                if (source[index] == '\\' && index + 1 < source.Length)
                {
                    Mask(source, index++);
                    Mask(source, index++);
                    continue;
                }

                char current = source[index];
                Mask(source, index++);
                if (current == '\'')
                {
                    break;
                }
            }

            return true;
        }

        private static void Mask(char[] source, int index)
        {
            if (source[index] != '\r' && source[index] != '\n')
            {
                source[index] = ' ';
            }
        }

        private static int GetLineNumber(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private static string MakeRelativePath(string root, string path)
        {
            return path.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string ReadAllText(string path)
        {
            return File.ReadAllText(ToFileSystemPath(path));
        }

        private static bool FileExists(string path)
        {
            return File.Exists(ToFileSystemPath(path));
        }

        private static string ToFileSystemPath(string path)
        {
            if (Path.DirectorySeparatorChar != '\\' ||
                !Path.IsPathRooted(path) ||
                path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return path;
            }

            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return @"\\?\UNC\" + path.Substring(2);
            }

            return @"\\?\" + path;
        }

        private sealed class ArchitectureSnapshot
        {
            private ArchitectureSnapshot(
                string cycloneGamesRoot,
                AssemblyDefinition[] assemblies,
                Dictionary<string, AssemblyDefinition> byName,
                Dictionary<string, string> nameByGuid)
            {
                CycloneGamesRoot = cycloneGamesRoot;
                Assemblies = assemblies;
                ByName = byName;
                NameByGuid = nameByGuid;
            }

            internal string CycloneGamesRoot { get; }
            internal AssemblyDefinition[] Assemblies { get; }
            internal Dictionary<string, AssemblyDefinition> ByName { get; }
            internal Dictionary<string, string> NameByGuid { get; }

            internal static ArchitectureSnapshot Load()
            {
                string root = FindCycloneGamesRoot();
                var definitions = new List<AssemblyDefinition>();
                var byName = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);
                var nameByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (string asmdefPath in Directory.EnumerateFiles(root, "*.asmdef", SearchOption.AllDirectories))
                {
                    string json = ReadAllText(asmdefPath);
                    Match nameMatch = NamePattern.Match(json);
                    if (!nameMatch.Success)
                    {
                        throw new InvalidDataException("Missing asmdef name: " + asmdefPath);
                    }

                    string name = nameMatch.Groups["value"].Value;
                    var definition = new AssemblyDefinition(
                        name,
                        asmdefPath,
                        ReadBoolean(json, "noEngineReferences", false),
                        ReadBoolean(json, "autoReferenced", true),
                        ReadReferences(json),
                        IsExemptDefinition(name, asmdefPath, root));

                    definitions.Add(definition);
                    byName.Add(name, definition);

                    string metaPath = asmdefPath + ".meta";
                    if (FileExists(metaPath))
                    {
                        Match guidMatch = Regex.Match(
                            ReadAllText(metaPath),
                            @"(?m)^guid:\s*(?<value>[A-Fa-f0-9]+)\s*$",
                            RegexOptions.CultureInvariant);
                        if (guidMatch.Success)
                        {
                            nameByGuid[guidMatch.Groups["value"].Value] = name;
                        }
                    }
                }

                return new ArchitectureSnapshot(root, definitions.ToArray(), byName, nameByGuid);
            }

            private static string FindCycloneGamesRoot()
            {
                string current = Path.GetFullPath(Directory.GetCurrentDirectory());
                while (!string.IsNullOrEmpty(current))
                {
                    string projectCandidate = Path.Combine(current, "Assets", "ThirdParty", "CycloneGames");
                    if (Directory.Exists(projectCandidate))
                    {
                        return Path.GetFullPath(projectCandidate);
                    }

                    string repositoryCandidate = Path.Combine(
                        current,
                        "UnityStarter",
                        "Assets",
                        "ThirdParty",
                        "CycloneGames");
                    if (Directory.Exists(repositoryCandidate))
                    {
                        return Path.GetFullPath(repositoryCandidate);
                    }

                    DirectoryInfo parent = Directory.GetParent(current);
                    current = parent?.FullName;
                }

                throw new DirectoryNotFoundException(
                    "Could not locate Assets/ThirdParty/CycloneGames from the current working directory.");
            }

            private static bool ReadBoolean(string json, string propertyName, bool defaultValue)
            {
                Match match = Regex.Match(
                    json,
                    "\\\"" + Regex.Escape(propertyName) + "\\\"\\s*:\\s*(?<value>true|false)",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                return match.Success
                    ? string.Equals(match.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase)
                    : defaultValue;
            }

            private static string[] ReadReferences(string json)
            {
                Match referencesMatch = ReferencesPattern.Match(json);
                if (!referencesMatch.Success)
                {
                    return Array.Empty<string>();
                }

                return JsonStringPattern.Matches(referencesMatch.Groups["value"].Value)
                    .Cast<Match>()
                    .Select(match => match.Groups["value"].Value)
                    .ToArray();
            }

            private static bool IsExemptDefinition(string name, string path, string root)
            {
                string[] nameSegments = name.Split('.');
                for (int i = 0; i < nameSegments.Length; i++)
                {
                    if (IsExemptSegment(nameSegments[i]))
                    {
                        return true;
                    }
                }

                string relative = MakeRelativePath(root, path);
                string[] pathSegments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                for (int i = 0; i < pathSegments.Length - 1; i++)
                {
                    if (IsExemptSegment(pathSegments[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class AssemblyDefinition
        {
            internal AssemblyDefinition(
                string name,
                string path,
                bool noEngineReferences,
                bool autoReferenced,
                string[] references,
                bool isExempt)
            {
                Name = name;
                Path = path;
                NoEngineReferences = noEngineReferences;
                AutoReferenced = autoReferenced;
                References = references;
                IsExempt = isExempt;
            }

            internal string Name { get; }
            internal string Path { get; }
            internal bool NoEngineReferences { get; }
            internal bool AutoReferenced { get; }
            internal string[] References { get; }
            internal bool IsExempt { get; }
        }
    }
}
