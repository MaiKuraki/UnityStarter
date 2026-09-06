using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using CycloneGames.Analyzers;

namespace CycloneGames.Analyzers.Tests
{
    /// <summary>
    /// CG0048 (StaticClassCircularDependency) must report mutual call cycles between static classes and
    /// stay silent once the shared logic is extracted into a dependency-free leaf static helper — the
    /// shape used by AssetRuntimeGuard / HandleTracker / SceneTracker / AssetRuntimeAssertions.
    /// </summary>
    [TestFixture]
    public sealed class StaticClassCircularDependencyAnalyzerTests
    {
        [Test]
        public async Task TwoStaticClassesCallingEachOtherAreReported()
        {
            const string source = """
                public static class Alpha
                {
                    public static void Ping()
                    {
                        Beta.Pong();
                    }
                }

                public static class Beta
                {
                    public static void Pong()
                    {
                        Alpha.Ping();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            List<Diagnostic> cycleDiagnostics = SelectCircularDependency(diagnostics);
            Assert.That(cycleDiagnostics, Has.Count.EqualTo(2),
                "Every invocation site inside the cycle must be reported once.");
            Assert.That(cycleDiagnostics.All(d => d.GetMessage().Contains("Alpha")),
                "The cycle description must name the classes forming the cycle.");
        }

        [Test]
        public async Task ThreeStaticClassCycleIsDeduplicatedToCanonicalForm()
        {
            const string source = """
                public static class A
                {
                    public static void Ping() => B.Pong();
                }

                public static class B
                {
                    public static void Pong() => C.Pang();
                }

                public static class C
                {
                    public static void Pang() => A.Ping();
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(SelectCircularDependency(diagnostics), Has.Count.EqualTo(3));
        }

        [Test]
        public async Task LeafHelperExtractionBreaksTheCycleWithoutDiagnostics()
        {
            const string source = """
                public static class Alpha
                {
                    public static void Ping()
                    {
                        Beta.Pong();
                    }
                }

                public static class Beta
                {
                    public static void Pong()
                    {
                        Shared.Leaf();
                    }
                }

                public static class Shared
                {
                    public static void Leaf()
                    {
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(SelectCircularDependency(diagnostics), Is.Empty);
        }

        private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp9));
            var compilation = CSharpCompilation.Create(
                "StaticClassCircularDependency",
                new[] { tree },
                LoadPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return await compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(
                    new StaticClassCircularDependencyAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();
        }

        private static List<Diagnostic> SelectCircularDependency(ImmutableArray<Diagnostic> diagnostics)
        {
            return diagnostics
                .Where(diagnostic => diagnostic.Id == "CG0048")
                .ToList();
        }

        private static ImmutableArray<MetadataReference> LoadPlatformReferences()
        {
            string? trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrEmpty(trustedAssemblies))
            {
                throw new IOException("Trusted platform assemblies are unavailable.");
            }

            return trustedAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToImmutableArray<MetadataReference>();
        }
    }
}
