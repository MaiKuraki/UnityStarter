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
    public sealed class ConventionAnalyzerTests
    {
        private const string AssemblyName = "CycloneGames.Utility.Runtime";
        private const string SourcePath = "Assets/ThirdParty/CycloneGames/CycloneGames.Utility/Runtime/Consumer.cs";

        private static readonly ImmutableArray<MetadataReference> PlatformReferences = LoadPlatformReferences();
        private static readonly MetadataReference FrameworkReference = CreateFrameworkReference();

        [Test]
        public async Task ReportsPublicInstanceFieldOnMonoBehaviourSubclass()
        {
            const string source = """
                public sealed class Consumer : UnityEngine.MonoBehaviour
                {
                    public int Health;
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.PublicFieldOnMonoBehaviour);
        }

        [Test]
        public async Task ReportsEachVariableOfMultiFieldDeclaration()
        {
            const string source = """
                public sealed class Consumer : UnityEngine.MonoBehaviour
                {
                    public int First, Second;
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            AssertDiagnosticIds(diagnostics,
                DiagnosticIds.PublicFieldOnMonoBehaviour,
                DiagnosticIds.PublicFieldOnMonoBehaviour);
        }

        [Test]
        public async Task DoesNotReportPublicFieldOnNestedReadOnlyStruct()
        {
            // Regression: TransformKeyRegistry.RuntimeEntry and FPSCounter.SortedFpsColor declare
            // public readonly fields on private nested structs. The enclosing MonoBehaviour must
            // not classify those fields as MonoBehaviour fields.
            const string source = """
                public sealed class Consumer : UnityEngine.MonoBehaviour
                {
                    private readonly struct RuntimeEntry
                    {
                        public readonly ulong Hash;
                        public readonly string Key;
                        public readonly int SourceIndex;

                        public RuntimeEntry(ulong hash, string key, int sourceIndex)
                        {
                            Hash = hash;
                            Key = key;
                            SourceIndex = sourceIndex;
                        }
                    }

                    private void Use()
                    {
                        _ = new RuntimeEntry(1UL, "key", 0);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public async Task DoesNotReportPublicFieldOnNestedSerializableStruct()
        {
            // Regression: FPSCounter.FPSColor is a designer-facing serializable POD struct;
            // public fields are the standard Unity authoring pattern for this shape.
            const string source = """
                public sealed class Consumer : UnityEngine.MonoBehaviour
                {
                    [System.Serializable]
                    public struct FPSColor
                    {
                        public int FPSValue;
                        public UnityEngine.Color Color;
                    }

                    private void Use()
                    {
                        _ = new FPSColor();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public async Task DoesNotReportPublicFieldOnNestedPlainClass()
        {
            const string source = """
                public sealed class Consumer : UnityEngine.MonoBehaviour
                {
                    public sealed class Config
                    {
                        public int Size;
                    }

                    private void Use()
                    {
                        _ = new Config();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public async Task ReportsPublicFieldOnNestedMonoBehaviourSubclass()
        {
            const string source = """
                public sealed class Consumer : UnityEngine.MonoBehaviour
                {
                    public sealed class Nested : UnityEngine.MonoBehaviour
                    {
                        public int Health;
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            AssertDiagnosticIds(diagnostics, DiagnosticIds.PublicFieldOnMonoBehaviour);
        }

        [Test]
        public async Task DoesNotReportConstStaticAndPrivateFields()
        {
            const string source = """
                public sealed class Consumer : UnityEngine.MonoBehaviour
                {
                    public const int Maximum = 10;
                    public static int Counter;
                    [UnityEngine.SerializeField]
                    private int _health;
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);

            Assert.That(diagnostics, Is.Empty);
        }

        [TestCase("Assets/ThirdParty/CycloneGames/CycloneGames.Utility/Editor/Consumer.cs")]
        [TestCase("Assets/ThirdParty/CycloneGames/CycloneGames.Utility/Tests/Consumer.cs")]
        public async Task DoesNotReportInAllowlistedSourcePath(string sourcePath)
        {
            const string source = """
                public sealed class Consumer : UnityEngine.MonoBehaviour
                {
                    public int Health;
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source, sourcePath: sourcePath);

            Assert.That(diagnostics, Is.Empty);
        }

        private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
            string source,
            string sourcePath = SourcePath)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);
            sourcePath = AnalyzerTestPaths.ResolveProjectRelativePath(sourcePath);
            SyntaxTree sourceTree = CSharpSyntaxTree.ParseText(source, parseOptions, sourcePath);

            var references = PlatformReferences.Add(FrameworkReference);
            CSharpCompilation compilation = CSharpCompilation.Create(
                AssemblyName,
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

            ImmutableArray<Diagnostic> diagnostics = await compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ConventionAnalyzer()))
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
                    public sealed class SerializeFieldAttribute : System.Attribute
                    {
                    }

                    public struct Color
                    {
                    }

                    public class MonoBehaviour
                    {
                    }
                }
                """;

            SyntaxTree frameworkTree = CSharpSyntaxTree.ParseText(
                frameworkSource,
                new CSharpParseOptions(LanguageVersion.CSharp10),
                "FrameworkStubs.cs");

            CSharpCompilation frameworkCompilation = CSharpCompilation.Create(
                "CycloneGames.Analyzers.Tests.FrameworkStubs.Convention",
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
