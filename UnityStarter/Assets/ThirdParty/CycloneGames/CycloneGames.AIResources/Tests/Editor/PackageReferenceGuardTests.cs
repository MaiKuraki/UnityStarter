using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AIResources.Tests.Editor
{
    /// <summary>
    /// Guards the packaging rule for the whole CycloneGames tree: an assembly may reference another
    /// package only when either its package declares that dependency, or the assembly is gated by a
    /// capability symbol. Everything else breaks a partial UPM install.
    /// </summary>
    /// <remarks>
    /// This lives in one package's test assembly but scans the repository, because the invariant is
    /// about the package graph rather than about AI.
    /// </remarks>
    public sealed class PackageReferenceGuardTests
    {
        private static readonly Regex PackageIdPattern = new Regex(
            "\"name\"\\s*:\\s*\"(com\\.cyclone-games\\.[a-z0-9.\\-]+)\"",
            RegexOptions.Compiled);

        private static readonly Regex DependencyKeyPattern = new Regex(
            "\"(com\\.cyclone-games\\.[a-z0-9.\\-]+)\"\\s*:\\s*\"",
            RegexOptions.Compiled);

        [Test]
        public void CrossPackageReferences_AreDeclaredOrGated()
        {
            string root = Path.Combine(Application.dataPath, "ThirdParty", "CycloneGames");
            Assert.IsTrue(Directory.Exists(root), "CycloneGames tree was not found at " + root);

            var packageIdOfDirectory = new Dictionary<string, string>(StringComparer.Ordinal);
            var dependenciesOfPackage = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var directoryOfAssembly = new Dictionary<string, string>(StringComparer.Ordinal);
            var modelOfAssembly = new Dictionary<string, AsmdefModel>(StringComparer.Ordinal);

            foreach (string directory in Directory.GetDirectories(root))
            {
                string packageJson = Path.Combine(directory, "package.json");
                if (!File.Exists(packageJson))
                {
                    continue;
                }

                string directoryName = Path.GetFileName(directory);
                string text = File.ReadAllText(packageJson);
                Match id = PackageIdPattern.Match(text);
                if (!id.Success)
                {
                    continue;
                }

                packageIdOfDirectory[directoryName] = id.Groups[1].Value;

                var dependencies = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match match in DependencyKeyPattern.Matches(text))
                {
                    dependencies.Add(match.Groups[1].Value);
                }

                dependenciesOfPackage[directoryName] = dependencies;

                foreach (string asmdefPath in Directory.GetFiles(directory, "*.asmdef", SearchOption.AllDirectories))
                {
                    AsmdefModel model = JsonUtility.FromJson<AsmdefModel>(File.ReadAllText(asmdefPath));
                    if (model?.name == null)
                    {
                        continue;
                    }

                    directoryOfAssembly[model.name] = directoryName;
                    modelOfAssembly[model.name] = model;
                }
            }

            Assert.Greater(directoryOfAssembly.Count, 0, "no assembly definition was found");

            var violations = new List<string>();
            foreach (KeyValuePair<string, AsmdefModel> entry in modelOfAssembly)
            {
                string assembly = entry.Key;
                AsmdefModel model = entry.Value;
                string ownerDirectory = directoryOfAssembly[assembly];

                if (IsGated(model))
                {
                    continue;
                }

                foreach (string reference in model.references ?? Array.Empty<string>())
                {
                    if (!directoryOfAssembly.TryGetValue(reference, out string targetDirectory) ||
                        string.Equals(targetDirectory, ownerDirectory, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string targetPackageId = packageIdOfDirectory[targetDirectory];
                    if (dependenciesOfPackage[ownerDirectory].Contains(targetPackageId))
                    {
                        continue;
                    }

                    violations.Add(
                        $"{assembly} references {reference} from {targetDirectory}, but " +
                        $"{ownerDirectory}/package.json does not declare {targetPackageId} and the assembly has no capability gate.");
                }
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        private static bool IsGated(AsmdefModel model)
        {
            string[] constraints = model.defineConstraints;
            VersionDefine[] versionDefines = model.versionDefines;
            if (constraints == null || constraints.Length == 0 || versionDefines == null)
            {
                return false;
            }

            foreach (VersionDefine versionDefine in versionDefines)
            {
                if (versionDefine?.define == null)
                {
                    continue;
                }

                foreach (string constraint in constraints)
                {
                    if (string.Equals(constraint, versionDefine.define, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        [Serializable]
        private sealed class AsmdefModel
        {
            public string name;
            public string[] references;
            public string[] defineConstraints;
            public VersionDefine[] versionDefines;
        }

        [Serializable]
        private sealed class VersionDefine
        {
            public string name;
            public string define;
        }
    }
}
