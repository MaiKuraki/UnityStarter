using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor.Integrations.DeterministicMath
{
    /// <summary>
    /// Guards the assembly layout contract: an assembly that declares
    /// <c>noEngineReferences: true</c> must live under a <c>Core/</c> directory.
    ///
    /// The rule is not cosmetic. A pure C# assembly placed under <c>Runtime/</c> tells every
    /// reader that the layer is Unity-bound while the compiler treats it as portable, and that
    /// misinformation is exactly what caused the DeterministicMath package to be read as
    /// engine-coupled during the 2026-11-15 audit.
    /// </summary>
    public sealed class InteractionAssemblyLayoutTests
    {
        private const string PackageRootRelative = "Assets/ThirdParty/CycloneGames";

        /// <summary>
        /// Resolves the project root without UnityEngine: this test assembly declares
        /// noEngineReferences, which is precisely the constraint it verifies. Test assemblies are
        /// loaded from &lt;project&gt;/Library/ScriptAssemblies.
        /// </summary>
        private static string ResolvePackageRoot()
        {
            // Unity loads assemblies from a stream, so Assembly.Location is empty and cannot be used to
            // walk up to the project. The editor's working directory is the project root.
            string projectRoot = Directory.GetCurrentDirectory();
            return Path.Combine(projectRoot, PackageRootRelative.Replace('/', Path.DirectorySeparatorChar));
        }

        [Test]
        public void PureCoreAssemblies_LiveUnderCoreDirectory()
        {
            string root = ResolvePackageRoot();
            Assert.That(Directory.Exists(root), Is.True, "package root not found: " + root);

            var offences = new List<string>();
            int scanned = 0;
            int pure = 0;

            foreach (string asmdef in Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories))
            {
                string relative = AsRelative(root, asmdef);
                if (!relative.StartsWith("CycloneGames.RPGFoundation.Interaction", StringComparison.Ordinal))
                {
                    continue;
                }

                // Generated or third-party mirrors are out of scope; only first-party interaction packages count.
                if (relative.Contains("/Library/") || relative.Contains("/obj/"))
                {
                    continue;
                }

                // Test assemblies legitimately declare noEngineReferences while living under Tests/Editor.
                if (relative.Contains("/Tests/"))
                {
                    continue;
                }

                scanned++;
                string json = File.ReadAllText(asmdef);
                if (!json.Contains("\"noEngineReferences\": true"))
                {
                    continue;
                }

                pure++;
                string directory = Path.GetFileName(Path.GetDirectoryName(asmdef));
                if (!string.Equals(directory, "Core", StringComparison.Ordinal))
                {
                    offences.Add(relative + " declares noEngineReferences but sits in '" + directory + "/'");
                }
            }

            Assert.That(scanned, Is.GreaterThan(0), "no interaction asmdef found under " + root);
            Assert.That(offences, Is.Empty,
                "pure C# assemblies must live under Core/. Offenders:\n" + string.Join("\n", offences));
            Assert.That(pure, Is.GreaterThan(0), "expected at least one pure C# interaction assembly");
        }

        [Test]
        public void AssemblyFileNames_MatchDeclaredAssemblyNames()
        {
            string root = ResolvePackageRoot();
            var offences = new List<string>();

            foreach (string asmdef in Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories))
            {
                string relative = AsRelative(root, asmdef);
                if (!relative.StartsWith("CycloneGames.RPGFoundation.Interaction", StringComparison.Ordinal))
                {
                    continue;
                }

                string json = File.ReadAllText(asmdef);
                int nameIndex = json.IndexOf("\"name\"", StringComparison.Ordinal);
                if (nameIndex < 0)
                {
                    offences.Add(relative + " has no name field");
                    continue;
                }

                int colon = json.IndexOf(':', nameIndex);
                int firstQuote = json.IndexOf('"', colon + 1);
                int secondQuote = json.IndexOf('"', firstQuote + 1);
                if (firstQuote < 0 || secondQuote < 0)
                {
                    offences.Add(relative + " has a malformed name field");
                    continue;
                }

                string declared = json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                string fileStem = Path.GetFileNameWithoutExtension(asmdef);
                if (!string.Equals(declared, fileStem, StringComparison.Ordinal))
                {
                    offences.Add(relative + ": file '" + fileStem + ".asmdef' declares name '" + declared + "'");
                }
            }

            Assert.That(offences, Is.Empty,
                "asmdef file name must equal its declared assembly name. Offenders:\n" + string.Join("\n", offences));
        }

        [Test]
        public void InteractionRuntime_DoesNotReferenceObjectPoolFrameworks()
        {
            // Interaction owns detection, state and authority. Object pooling and VFX playback are
            // product concerns; binding the runtime to a pool framework forces every consumer to
            // adopt that framework, including headless and DI-only hosts.
            string root = ResolvePackageRoot();
            string asmdefPath = Path.Combine(root,
                "CycloneGames.RPGFoundation", "Interaction", "Runtime",
                "CycloneGames.RPGFoundation.Interaction.Runtime.asmdef");

            Assert.That(File.Exists(asmdefPath), Is.True, "runtime asmdef not found: " + asmdefPath);

            string json = File.ReadAllText(asmdefPath);
            var offenders = new List<string>();
            foreach (string forbidden in ForbiddenRuntimeDependencies)
            {
                if (json.Contains("\"" + forbidden))
                {
                    offenders.Add(forbidden);
                }
            }

            Assert.That(offenders, Is.Empty,
                "Interaction.Runtime must stay free of object-pool frameworks. Offenders: " + string.Join(", ", offenders));
        }

        private static readonly string[] ForbiddenRuntimeDependencies =
        {
            "CycloneGames.Factory",
        };

        private static string AsRelative(string root, string full)
        {
            string normalized = full.Replace('\\', '/');
            string normalizedRoot = root.Replace('\\', '/').TrimEnd('/');
            return normalized.StartsWith(normalizedRoot, StringComparison.Ordinal)
                ? normalized.Substring(normalizedRoot.Length).TrimStart('/')
                : normalized;
        }
    }
}
