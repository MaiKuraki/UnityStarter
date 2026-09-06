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
    /// CG0014 (ResourcesLoad) must keep reporting gameplay code that bypasses the asset pipeline, while
    /// exempting the pipeline's own provider implementations (IAssetPackage / IAssetModule implementers,
    /// including custom providers such as xasset adapters) whose sanctioned job is to wrap Resources.Load*.
    /// </summary>
    [TestFixture]
    public sealed class AssetPipelineResourceLoadExemptionTests
    {
        private const string StubHeader = """
            namespace UnityEngine
            {
                public class Object { }

                public static class Resources
                {
                    public static T Load<T>(string path) where T : Object => null;
                    public static T[] LoadAll<T>(string path) where T : Object => null;
                    public static RequestStub LoadAsync<T>(string path) where T : Object => null;
                }

                public sealed class RequestStub { }
            }

            namespace CycloneGames.AssetManagement.Runtime
            {
                // Name-matched provider contracts of the asset pipeline (unique in the repository).
                public interface IAssetPackage { }
                public interface IAssetModule { }
            }
            """;

        [Test]
        public async Task PlainStaticClassCallingResourcesLoadIsReported()
        {
            string source = StubHeader + """

                public static class PlainConsumer
                {
                    public static UnityEngine.Object Get() => UnityEngine.Resources.Load<UnityEngine.Object>("x");
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(SelectResourcesLoad(diagnostics), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task PlainClassCallingResourcesLoadAsyncIsReported()
        {
            string source = StubHeader + """

                public static class PlainAsyncConsumer
                {
                    public static void Get() => UnityEngine.Resources.LoadAsync<UnityEngine.Object>("x");
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(SelectResourcesLoad(diagnostics), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task AssetPackageImplementationCallingResourcesLoadIsExempted()
        {
            string source = StubHeader + """

                public sealed class PackageProvider : CycloneGames.AssetManagement.Runtime.IAssetPackage
                {
                    public UnityEngine.Object Get() => UnityEngine.Resources.Load<UnityEngine.Object>("x");
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(SelectResourcesLoad(diagnostics), Is.Empty);
        }

        [Test]
        public async Task AssetModuleImplementationCallingResourcesLoadAsyncIsExempted()
        {
            string source = StubHeader + """

                public sealed class ModuleProvider : CycloneGames.AssetManagement.Runtime.IAssetModule
                {
                    public void Get() => UnityEngine.Resources.LoadAsync<UnityEngine.Object>("x");
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(SelectResourcesLoad(diagnostics), Is.Empty);
        }

        [Test]
        public async Task NestedTypeInsideProviderImplementationIsExempted()
        {
            string source = StubHeader + """

                public sealed class ProviderWithNested : CycloneGames.AssetManagement.Runtime.IAssetPackage
                {
                    public static class Nested
                    {
                        public static UnityEngine.Object Get() => UnityEngine.Resources.Load<UnityEngine.Object>("x");
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(SelectResourcesLoad(diagnostics), Is.Empty);
        }

        [Test]
        public async Task DerivedProviderImplementationIsExempted()
        {
            string source = StubHeader + """

                public abstract class ProviderBase : CycloneGames.AssetManagement.Runtime.IAssetPackage { }

                public sealed class DerivedProvider : ProviderBase
                {
                    public UnityEngine.Object Get() => UnityEngine.Resources.Load<UnityEngine.Object>("x");
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(SelectResourcesLoad(diagnostics), Is.Empty);
        }

        [Test]
        public async Task NonProviderTypeImplementingAnUnrelatedInterfaceIsStillReported()
        {
            string source = StubHeader + """

                public interface IUnrelatedFeature { }

                public sealed class UnrelatedConsumer : IUnrelatedFeature
                {
                    public UnityEngine.Object Get() => UnityEngine.Resources.Load<UnityEngine.Object>("x");
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(SelectResourcesLoad(diagnostics), Has.Count.EqualTo(1));
        }

        private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp9));
            var compilation = CSharpCompilation.Create(
                "AssetPipelineResourceLoadExemption",
                new[] { tree },
                LoadPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return await compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(
                    new ForbiddenUnityApiAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();
        }

        private static List<Diagnostic> SelectResourcesLoad(ImmutableArray<Diagnostic> diagnostics)
        {
            return diagnostics
                .Where(diagnostic => diagnostic.Id == "CG0014")
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
