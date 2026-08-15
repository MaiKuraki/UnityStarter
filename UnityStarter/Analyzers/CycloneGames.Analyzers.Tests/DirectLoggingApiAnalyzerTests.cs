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

namespace CycloneGames.Analyzers.Tests
{
    [TestFixture]
    public sealed class DirectLoggingApiAnalyzerTests
    {
        private const string RuntimeAssembly = "CycloneGames.Feature.Runtime";
        private const string RuntimePath = "Assets/ThirdParty/CycloneGames/Feature/Runtime/Consumer.cs";
        private const string EditorAssembly = "CycloneGames.Feature.Editor";
        private const string EditorPath = "Assets/ThirdParty/CycloneGames/Feature/Editor/Consumer.cs";

        private static readonly ImmutableArray<MetadataReference> PlatformReferences = LoadPlatformReferences();
        private static readonly MetadataReference FrameworkReference = CreateFrameworkReference();

        [Test]
        public async Task ReportsUnityDebugLogAssertAndExceptionCalls()
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Run()
                    {
                        UnityEngine.Debug.Log("message");
                        UnityEngine.Debug.LogFormat("{0}", "message");
                        UnityEngine.Debug.LogWarning("message");
                        UnityEngine.Debug.LogWarningFormat("{0}", "message");
                        UnityEngine.Debug.LogError("message");
                        UnityEngine.Debug.LogErrorFormat("{0}", "message");
                        UnityEngine.Debug.Assert(false);
                        UnityEngine.Debug.AssertFormat(false, "{0}", "message");
                        UnityEngine.Debug.LogException(new System.Exception());
                        UnityEngine.Debug.LogAssertion("failure");
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            AssertDiagnosticIds(diagnostics,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi);
        }

        [Test]
        public async Task DoesNotReportNonLoggingUnityDebugMethods()
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Run()
                    {
                        UnityEngine.Debug.DrawLine();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public async Task ReportsMonoBehaviourPrint()
        {
            const string source = """
                public sealed class Consumer : UnityEngine.MonoBehaviour
                {
                    public void Run()
                    {
                        print("message");
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.DirectLoggingApi);
        }

        [Test]
        public async Task ReportsConsoleWritersAndConsoleStreams()
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Run()
                    {
                        System.Console.Write("a");
                        System.Console.WriteLine("b");
                        System.Console.Error.WriteLine("c");
                        System.Console.Out.Write("d");
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            AssertDiagnosticIds(diagnostics,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi);
        }

        [Test]
        public async Task ReportsUnityLoggerPropertyAccess()
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Run()
                    {
                        UnityEngine.Debug.unityLogger.Log("message");
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.DirectLoggingApi);
        }

        [Test]
        public async Task ReportsBackendLogPipelineConstructionAndAliasUses()
        {
            const string source = """
                using BackendPipeline = CycloneGames.Logging.Pipeline.LogPipeline;

                public sealed class Consumer
                {
                    private BackendPipeline _pipeline;

                    public BackendPipeline Current => _pipeline;

                    public void Run()
                    {
                        _pipeline = new BackendPipeline();
                        _pipeline = new CycloneGames.Logging.Pipeline.LogPipeline();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            AssertDiagnosticIds(diagnostics,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi);
        }

        [Test]
        public async Task DoesNotReportUnrelatedTypesWithMatchingShortNames()
        {
            const string source = """
                namespace Local
                {
                    public static class Debug
                    {
                        public static void Log(string message) { }
                    }

                    public static class Console
                    {
                        public static void WriteLine(string message) { }
                    }

                    public static class LogPipeline
                    {
                        public static void Write(string message) { }
                    }
                }

                public sealed class Consumer
                {
                    public void Run()
                    {
                        Local.Debug.Log("message");
                        Local.Console.WriteLine("message");
                        Local.LogPipeline.Write("message");
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public async Task DoesNotReportUnifiedLoggingAbstraction()
        {
            const string source = """
                namespace CycloneGames.Logging
                {
                    public interface ILogWriter
                    {
                        void Write(string message);
                    }
                }

                public sealed class Consumer
                {
                    private readonly CycloneGames.Logging.ILogWriter _log;

                    public Consumer(CycloneGames.Logging.ILogWriter log)
                    {
                        _log = log;
                    }

                    public void Run()
                    {
                        _log.Write("message");
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public async Task DoesNotReportOutsideCycloneGamesAssembly()
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Run()
                    {
                        UnityEngine.Debug.Log("message");
                        System.Console.WriteLine("message");
                        _ = new CycloneGames.Logging.Pipeline.LogPipeline();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                assemblyName: "Product.Runtime");

            Assert.That(diagnostics, Is.Empty);
        }

        [TestCase("CycloneGames.Feature.Tests.Runtime")]
        [TestCase("CycloneGames.Feature.Tools.CodeGen")]
        [TestCase("CycloneGames.Feature.CodeGen")]
        [TestCase("CycloneGames.Logging.Pipeline")]
        [TestCase("CycloneGames.Logging.Unity")]
        [TestCase("CycloneGames.Logging.Unity.Editor")]
        public async Task DoesNotReportInAllowlistedAssembly(string assemblyName)
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Run()
                    {
                        UnityEngine.Debug.Log("message");
                        System.Console.WriteLine("message");
                        _ = new CycloneGames.Logging.Pipeline.LogPipeline();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source, assemblyName: assemblyName);

            Assert.That(diagnostics, Is.Empty);
        }

        [TestCase("CycloneGames.MemoryGovernance.Logging.Pipeline")]
        [TestCase("CycloneGames.MemoryGovernance.Logging.Pipeline.Editor")]
        public async Task ReportsSimilarlyNamedBusinessAssembly(string assemblyName)
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Run()
                    {
                        _ = new CycloneGames.Logging.Pipeline.LogPipeline();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                assemblyName: assemblyName);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.DirectLoggingApi);
        }

        [TestCase(
            "CycloneGames.Feature.Samples",
            "Assets/ThirdParty/CycloneGames/Feature/Samples/Consumer.cs")]
        [TestCase(
            "CycloneGames.Feature.Benchmarks",
            "Assets/ThirdParty/CycloneGames/Feature/Benchmarks/Consumer.cs")]
        public async Task ReportsInCopyableSampleAndBenchmarkCode(
            string assemblyName,
            string sourcePath)
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Run()
                    {
                        UnityEngine.Debug.Log("message");
                        System.Console.WriteLine("message");
                        _ = new CycloneGames.Logging.Pipeline.LogPipeline();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                assemblyName: assemblyName,
                sourcePath: sourcePath);

            AssertDiagnosticIds(
                diagnostics,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi);
        }

        [Test]
        public async Task LoggingUnitySample_AllowsPipelineCompositionButStillReportsRawOutput()
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Run()
                    {
                        UnityEngine.Debug.Log("message");
                        System.Console.WriteLine("message");
                        _ = new CycloneGames.Logging.Pipeline.LogPipeline();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                assemblyName: "CycloneGames.Logging.Unity.Samples",
                sourcePath: "Assets/ThirdParty/CycloneGames/CycloneGames.Logging.Unity/Samples/Consumer.cs");

            AssertDiagnosticIds(
                diagnostics,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi);
        }

        [TestCase(EditorAssembly, RuntimePath)]
        [TestCase(RuntimeAssembly, EditorPath)]
        [TestCase(EditorAssembly, EditorPath)]
        public async Task ReportsInCycloneGamesEditorAssemblyAndSourcePath(
            string assemblyName,
            string sourcePath)
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Run()
                    {
                        UnityEngine.Debug.Log("message");
                        System.Console.WriteLine("message");
                        _ = new CycloneGames.Logging.Pipeline.LogPipeline();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                assemblyName: assemblyName,
                sourcePath: sourcePath);

            AssertDiagnosticIds(
                diagnostics,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi,
                DiagnosticIds.DirectLoggingApi);
        }

        [TestCase("Assets/ThirdParty/CycloneGames/Feature/Test/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames/Feature/Test~/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames/Feature/Tests/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames/Feature/Tests~/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames/Feature/Tool/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames/Feature/Tool~/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames/Feature/Tools/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames/Feature/Tools~/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames/Feature/CodeGen/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames/Feature/CodeGen~/Consumer.cs")]
        public async Task DoesNotReportInAllowlistedSourcePath(string sourcePath)
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Run()
                    {
                        UnityEngine.Debug.Log("message");
                        System.Console.WriteLine("message");
                        _ = new CycloneGames.Logging.Pipeline.LogPipeline();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source, sourcePath: sourcePath);

            Assert.That(diagnostics, Is.Empty);
        }

        [TestCase(RuntimeAssembly, RuntimePath)]
        [TestCase(EditorAssembly, EditorPath)]
        public async Task DoesNotDuplicateHotPathDebugDiagnosticInGovernedCycloneGamesAssembly(
            string assemblyName,
            string sourcePath)
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Update()
                    {
                        UnityEngine.Debug.Log("message");
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                assemblyName: assemblyName,
                sourcePath: sourcePath,
                includeHotPathAnalyzer: true);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.DirectLoggingApi);
        }

        [Test]
        public async Task PreservesHotPathDebugDiagnosticOutsideCycloneGamesRuntime()
        {
            const string source = """
                public sealed class Consumer
                {
                    public void Update()
                    {
                        UnityEngine.Debug.Log("message");
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
                source,
                assemblyName: "Product.Runtime",
                includeHotPathAnalyzer: true);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.DebugLogInHotPath);
        }

        private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
            string source,
            string assemblyName = RuntimeAssembly,
            string sourcePath = RuntimePath,
            bool includeHotPathAnalyzer = false)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);
            sourcePath = AnalyzerTestPaths.ResolveProjectRelativePath(sourcePath);
            SyntaxTree sourceTree = CSharpSyntaxTree.ParseText(source, parseOptions, sourcePath);

            var references = PlatformReferences.Add(FrameworkReference);
            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { sourceTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            ImmutableArray<Diagnostic> compilerErrors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();

            Assert.That(
                compilerErrors,
                Is.Empty,
                "Test source must compile before analyzer execution:" + Environment.NewLine +
                string.Join(Environment.NewLine, compilerErrors));

            ImmutableArray<DiagnosticAnalyzer> analyzers = includeHotPathAnalyzer
                ? ImmutableArray.Create<DiagnosticAnalyzer>(
                    new DirectLoggingApiAnalyzer(),
                    new HotPathUnityBestPracticeAnalyzer())
                : ImmutableArray.Create<DiagnosticAnalyzer>(new DirectLoggingApiAnalyzer());

            ImmutableArray<Diagnostic> diagnostics = await compilation
                .WithAnalyzers(analyzers)
                .GetAnalyzerDiagnosticsAsync();

            return diagnostics
                .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
                .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
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

        private static MetadataReference CreateFrameworkReference()
        {
            const string frameworkSource = """
                namespace UnityEngine
                {
                    public interface ILogger
                    {
                        void Log(object message);
                    }

                    public static class Debug
                    {
                        public static ILogger unityLogger { get; }

                        public static void Log(object message) { }
                        public static void LogFormat(string format, params object[] args) { }
                        public static void LogWarning(object message) { }
                        public static void LogWarningFormat(string format, params object[] args) { }
                        public static void LogError(object message) { }
                        public static void LogErrorFormat(string format, params object[] args) { }
                        public static void LogException(System.Exception exception) { }
                        public static void LogAssertion(object message) { }
                        public static void Assert(bool condition) { }
                        public static void AssertFormat(bool condition, string format, params object[] args) { }
                        public static void DrawLine() { }
                    }

                    public class MonoBehaviour
                    {
                        public static void print(object message) { }
                    }
                }

                namespace CycloneGames.Logging.Pipeline
                {
                    public sealed class LogPipeline
                    {
                    }
                }
                """;

            SyntaxTree frameworkTree = CSharpSyntaxTree.ParseText(
                frameworkSource,
                new CSharpParseOptions(LanguageVersion.CSharp10),
                "FrameworkStubs.cs");

            CSharpCompilation frameworkCompilation = CSharpCompilation.Create(
                "CycloneGames.Analyzers.Tests.FrameworkStubs",
                new[] { frameworkTree },
                PlatformReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var emitResult = frameworkCompilation.Emit(stream);
            if (!emitResult.Success)
            {
                throw new InvalidOperationException(
                    "Framework stubs failed to compile:" + Environment.NewLine +
                    string.Join(Environment.NewLine, emitResult.Diagnostics));
            }

            return MetadataReference.CreateFromImage(stream.ToArray());
        }
    }
}
