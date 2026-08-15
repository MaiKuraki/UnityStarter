using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace CycloneGames.Analyzers.Tests
{
    [TestFixture]
    public sealed class ModuleLogConventionAnalyzerTests
    {
        private const string RuntimeAssembly = "CycloneGames.Feature.Runtime";
        private const string RuntimePath =
            "Assets/ThirdParty/CycloneGames/Feature/Runtime/Consumer.cs";
        private const string FacadePath =
            "Assets/ThirdParty/CycloneGames/Feature/Runtime/Diagnostics/FeatureLog.cs";

        private static readonly ImmutableArray<MetadataReference> PlatformReferences =
            LoadPlatformReferences();

        [Test]
        public async Task ReportsCreateOutsideAssemblyFacade()
        {
            const string source = FrameworkSource + """

                public sealed class Consumer
                {
                    private static readonly CycloneGames.Logging.LogChannel Log =
                        CycloneGames.Logging.LogChannel.Create("CycloneGames.Feature");
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.ModuleLogConvention);
        }

        [Test]
        public async Task ReportsAliasedCreateOutsideAssemblyFacade()
        {
            const string source = FrameworkSource + """

                namespace Feature
                {
                    using Channel = CycloneGames.Logging.LogChannel;

                    public sealed class Consumer
                    {
                        private static readonly Channel Log =
                            Channel.Create("CycloneGames.Feature");
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.ModuleLogConvention);
        }

        [Test]
        public async Task AllowsStandardFacadeChannelAndExplicitWriterFactory()
        {
            const string source = FrameworkSource + """

                internal static class FeatureLog
                {
                    internal const string Category = "CycloneGames.Feature";
                    internal static readonly CycloneGames.Logging.LogChannel Channel =
                        CycloneGames.Logging.LogChannel.Create(Category);

                    internal static CycloneGames.Logging.LogChannel Create(
                        CycloneGames.Logging.ILogWriter logWriter)
                    {
                        return CycloneGames.Logging.LogChannel.Create(Category, logWriter);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                sourcePath: FacadePath);

            Assert.That(diagnostics, Is.Empty);
        }

        [TestCase("public static class FeatureLog", FacadePath)]
        [TestCase("internal static class FeatureDiagnostics", FacadePath)]
        [TestCase("internal static class FeatureLog", RuntimePath)]
        public async Task ReportsFacadeWithNonStandardDeclarationOrPath(
            string declaration,
            string sourcePath)
        {
            string source = FrameworkSource + $$"""

                {{declaration}}
                {
                    internal static readonly CycloneGames.Logging.LogChannel Channel =
                        CycloneGames.Logging.LogChannel.Create("CycloneGames.Feature");
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                sourcePath: sourcePath);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.ModuleLogConvention);
        }

        [Test]
        public async Task ReportsFacadeWithoutStandardCategoryMember()
        {
            const string source = FrameworkSource + """

                internal static class FeatureLog
                {
                    internal static readonly CycloneGames.Logging.LogChannel Channel =
                        CycloneGames.Logging.LogChannel.Create("CycloneGames.Feature");

                    internal static CycloneGames.Logging.LogChannel Create(
                        CycloneGames.Logging.ILogWriter logWriter)
                    {
                        return default;
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                sourcePath: FacadePath);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.ModuleLogConvention);
        }

        [Test]
        public async Task ReportsFacadeWithoutStandardChannelMember()
        {
            const string source = FrameworkSource + """

                internal static class FeatureLog
                {
                    internal const string Category = "CycloneGames.Feature";

                    internal static CycloneGames.Logging.LogChannel Create(
                        CycloneGames.Logging.ILogWriter logWriter)
                    {
                        return CycloneGames.Logging.LogChannel.Create(Category, logWriter);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                sourcePath: FacadePath);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.ModuleLogConvention);
        }

        [Test]
        public async Task ReportsFacadeWithoutStandardFactorySignature()
        {
            const string source = FrameworkSource + """

                internal static class FeatureLog
                {
                    internal const string Category = "CycloneGames.Feature";
                    internal static readonly CycloneGames.Logging.LogChannel Channel =
                        CycloneGames.Logging.LogChannel.Create(Category);

                    internal static CycloneGames.Logging.LogChannel Create(
                        CycloneGames.Logging.ILogWriter writer)
                    {
                        return default;
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                sourcePath: FacadePath);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.ModuleLogConvention);
        }

        [TestCase("Product.Runtime", RuntimePath)]
        [TestCase("CycloneGames.Feature.Tests.Editor", RuntimePath)]
        [TestCase("CycloneGames.Logging.Pipeline", RuntimePath)]
        public async Task DoesNotReportOutsideGovernedPackageScope(
            string assemblyName,
            string sourcePath)
        {
            const string source = FrameworkSource + """

                public sealed class Consumer
                {
                    private static readonly CycloneGames.Logging.LogChannel Log =
                        CycloneGames.Logging.LogChannel.Create("CycloneGames.Feature");
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                assemblyName,
                sourcePath);

            Assert.That(diagnostics, Is.Empty);
        }

        [TestCase(
            "CycloneGames.Feature.Samples",
            "Assets/ThirdParty/CycloneGames/Feature/Samples/Consumer.cs")]
        [TestCase(
            "CycloneGames.Feature.Benchmarks",
            "Assets/ThirdParty/CycloneGames/Feature/Benchmarks/Consumer.cs")]
        public async Task ReportsCreateOutsideFacadeInCopyableSampleAndBenchmarkCode(
            string assemblyName,
            string sourcePath)
        {
            const string source = FrameworkSource + """

                public sealed class Consumer
                {
                    private static readonly CycloneGames.Logging.LogChannel Log =
                        CycloneGames.Logging.LogChannel.Create("CycloneGames.Feature");
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                assemblyName,
                sourcePath);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.ModuleLogConvention);
        }

        private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
            string source,
            string assemblyName = RuntimeAssembly,
            string sourcePath = RuntimePath)
        {
            sourcePath = AnalyzerTestPaths.ResolveProjectRelativePath(sourcePath);
            SyntaxTree sourceTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp10),
                sourcePath);

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { sourceTree },
                PlatformReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            ImmutableArray<Diagnostic> compilerErrors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();

            Assert.That(
                compilerErrors,
                Is.Empty,
                "Test source must compile before analyzer execution:" + Environment.NewLine +
                string.Join(Environment.NewLine, compilerErrors));

            ImmutableArray<Diagnostic> diagnostics = await compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(
                        new ModuleLogConventionAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();

            return diagnostics
                .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
                .ToImmutableArray();
        }

        private static void AssertDiagnosticIds(
            ImmutableArray<Diagnostic> diagnostics,
            params string[] expectedIds)
        {
            Assert.That(
                diagnostics.Select(diagnostic => diagnostic.Id),
                Is.EqualTo(expectedIds),
                string.Join(Environment.NewLine, diagnostics));
        }

        private static ImmutableArray<MetadataReference> LoadPlatformReferences()
        {
            string? trustedPlatformAssemblies =
                AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToImmutableArray<MetadataReference>();
        }

        private const string FrameworkSource = """
            namespace CycloneGames.Logging
            {
                public interface ILogWriter
                {
                }

                public readonly struct LogChannel
                {
                    public static LogChannel Create(string category)
                    {
                        return default;
                    }

                    public static LogChannel Create(string category, ILogWriter logWriter)
                    {
                        return default;
                    }
                }
            }
            """;
    }
}
