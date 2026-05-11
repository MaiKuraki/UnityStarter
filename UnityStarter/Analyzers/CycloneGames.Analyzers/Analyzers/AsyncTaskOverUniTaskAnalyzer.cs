using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Flags <c>async Task</c> / <c>async Task<T></c> method return types
    /// in projects that reference UniTask. In those projects, UniTask (struct, poolable)
    /// is preferred over Task (class, always allocates) per the project's async convention.
    ///
    /// Safety: this analyzer only activates when the <c>Cysharp.Threading.Tasks</c>
    /// namespace is available in the compilation. Pure C# libraries without UniTask
    /// are never flagged.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class AsyncTaskOverUniTaskAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DiagnosticRules.AsyncTaskOverUniTask);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Only register the analyzer if UniTask is actually referenced by this compilation.
            // This ensures pure-C# / non-Unity libraries are never flagged.
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            // Check if UniTask assembly is in the compilation
            var hasUniTask = context.Compilation.ReferencedAssemblyNames
                .Any(a => a.Name == "UniTask" || a.Name.StartsWith("Cysharp.Threading.Tasks"));

            if (!hasUniTask) return;

            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            var methodDecl = (MethodDeclarationSyntax)context.Node;

            // Must be async
            if (!methodDecl.Modifiers.Any(SyntaxKind.AsyncKeyword)) return;

            var returnType = methodDecl.ReturnType;
            var symbolInfo = context.SemanticModel.GetSymbolInfo(returnType);
            if (symbolInfo.Symbol is not INamedTypeSymbol typeSymbol) return;

            var fullName = typeSymbol.ToString();
            if (fullName != "System.Threading.Tasks.Task" &&
                !fullName.StartsWith("System.Threading.Tasks.Task<"))
                return;

            // Skip Editor / Samples / Tests
            var filePath = methodDecl.SyntaxTree.FilePath.Replace('\\', '/');
            if (filePath.Contains("/Editor/") ||
                filePath.Contains("/Samples/") ||
                filePath.Contains("/Tests/")) return;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticRules.AsyncTaskOverUniTask,
                methodDecl.Identifier.GetLocation(),
                methodDecl.Identifier.Text));
        }
    }
}
