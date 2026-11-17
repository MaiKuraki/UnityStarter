using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Guard test for the package-local Unity compatibility shims.
    /// </summary>
    /// <remarks>
    /// Each CycloneGames package ships its own <c>UnityEngineIdentityShim.cs</c> so that no package has to
    /// reference another package's assembly. The duplication is only safe while every copy agrees on the
    /// Unity version boundaries and on the fallback APIs: a copy that lags silently changes behaviour for
    /// one package only. Copies may differ in which shims they include (runtime packages must not carry
    /// scene-wide queries), so this test compares the version-sensitive fragments rather than whole files.
    /// It lives next to <see cref="GASEngineSeamGuardTests"/> because the repository has no project-level
    /// editor test assembly and that test already guards repository-wide invariants.
    /// </remarks>
    public sealed class CompatShimConsistencyTests
    {
        private const string ContentRoot = "Assets/ThirdParty/CycloneGames";
        private const string ShimFileName = "UnityEngineIdentityShim.cs";
        private const string VersionBoundary = "UNITY_6000_6_OR_NEWER";

        private static readonly Regex VersionSymbol =
            new Regex(@"UNITY_\d+_\d+(_\d+)?_OR_NEWER", RegexOptions.Compiled);

        [Test]
        public void EveryPackageShim_UsesTheSameUnityVersionBoundary()
        {
            string[] shimPaths = FindShims();
            Assert.That(shimPaths.Length, Is.GreaterThan(0), "At least one package must carry the shim.");

            var offenders = new List<string>();
            for (int i = 0; i < shimPaths.Length; i++)
            {
                var symbols = new HashSet<string>();
                Match match = VersionSymbol.Match(StripLineComments(File.ReadAllText(shimPaths[i])));
                while (match.Success)
                {
                    symbols.Add(match.Value);
                    match = match.NextMatch();
                }

                if (symbols.Count != 1 || !symbols.Contains(VersionBoundary))
                {
                    offenders.Add($"{Normalize(shimPaths[i])} -> [{string.Join(", ", symbols)}]");
                }
            }

            Assert.That(offenders, Is.Empty,
                $"Every shim must branch only on {VersionBoundary}. Offenders:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void EveryPackageShim_DeclaresTheSharedFallbackApis()
        {
            string[] shimPaths = FindShims();
            Assert.That(shimPaths.Length, Is.GreaterThan(0), "At least one package must carry the shim.");

            var message = new StringBuilder();
            for (int i = 0; i < shimPaths.Length; i++)
            {
                string text = File.ReadAllText(shimPaths[i]);
                string path = Normalize(shimPaths[i]);

                if (text.Contains("class UnityObjectId") &&
                    (!text.Contains("EntityId.ToULong(target.GetEntityId())") ||
                     !text.Contains("unchecked((ulong)(uint)target.GetInstanceID())")))
                {
                    message.Append("UnityObjectId must read EntityId on new editors and widen the legacy id: ")
                        .AppendLine(path);
                }

                if (text.Contains("class UnityObjectQuery") &&
                    (!text.Contains("Object.FindObjectsByType<T>(inactive)") ||
                     !text.Contains("Object.FindObjectsByType<T>(inactive, FindObjectsSortMode.None)")))
                {
                    message.Append("UnityObjectQuery must use the sort-free overload only on new editors: ")
                        .AppendLine(path);
                }
            }

            Assert.That(message.Length, Is.Zero, message.ToString());
        }


        /// <summary>
        /// Drops <c>//</c> comments before scanning. The shims explain in prose which editor versions
        /// changed an API, and a symbol named in a comment is documentation, not a branch.
        /// </summary>
        private static string StripLineComments(string text)
        {
            var builder = new StringBuilder(text.Length);
            int start = 0;
            while (start < text.Length)
            {
                int end = text.IndexOf('\n', start);
                if (end < 0)
                    end = text.Length;

                int comment = text.IndexOf("//", start, end - start, StringComparison.Ordinal);
                int lineEnd = comment >= 0 ? comment : end;
                builder.Append(text, start, lineEnd - start).Append('\n');

                start = end + 1;
            }

            return builder.ToString();
        }

        private static string[] FindShims()
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), ContentRoot);
            Assert.That(Directory.Exists(root), Is.True, $"Expected shim root missing: {root}");
            return Directory.GetFiles(root, ShimFileName, SearchOption.AllDirectories);
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
