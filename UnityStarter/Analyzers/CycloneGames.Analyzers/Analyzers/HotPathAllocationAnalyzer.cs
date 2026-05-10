using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CycloneGames.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class HotPathAllocationAnalyzer : DiagnosticAnalyzer
    {
        private static readonly string[] HotPathMethodNames =
        {
            "Update", "LateUpdate", "FixedUpdate",
            "OnTick", "Tick", "PreUpdate", "PostUpdate",
            "OnUpdate", "OnPreTick", "OnPostTick"
        };

        private static readonly string[] LinqMethodNames =
        {
            "Where", "Select", "SelectMany", "First", "FirstOrDefault",
            "Last", "LastOrDefault", "Single", "SingleOrDefault",
            "Any", "All", "Count", "Sum", "Average", "Min", "Max",
            "ToList", "ToArray", "ToDictionary", "ToHashSet",
            "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
            "GroupBy", "Join", "GroupJoin", "Take", "Skip",
            "Distinct", "Reverse", "Except", "Intersect", "Union",
            "OfType", "Cast", "Aggregate", "Zip", "ElementAt"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            DiagnosticRules.HotPathForEach,
            DiagnosticRules.HotPathLinq,
            DiagnosticRules.HotPathStringConcat);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            var methodDecl = (MethodDeclarationSyntax)context.Node;
            var methodName = methodDecl.Identifier.Text;

            if (!IsHotPathMethod(methodName)) return;

            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDecl);
            if (methodSymbol == null) return;

            // 1. Check foreach (skip value-type enumerators — zero GC in IL2CPP)
            foreach (var forEachStmt in methodDecl.DescendantNodes().OfType<ForEachStatementSyntax>())
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(forEachStmt.Expression);
                var fullTypeName = typeInfo.Type?.ToString() ?? "";

                // Value-type enumerators produce zero GC — safe in hot paths
                if (IsZeroAllocationEnumerator(fullTypeName)) continue;

                var typeName = typeInfo.Type?.Name ?? "collection";
                var diagnostic = Diagnostic.Create(
                    DiagnosticRules.HotPathForEach,
                    forEachStmt.ForEachKeyword.GetLocation(),
                    methodName, typeName);
                context.ReportDiagnostic(diagnostic);
            }

            // 2. Check LINQ (MemberAccessExpression that chains LINQ methods)
            foreach (var invocation in methodDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var invokedMethodName = memberAccess.Name.Identifier.Text;
                    if (LinqMethodNames.Contains(invokedMethodName))
                    {
                        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
                        if (symbolInfo.Symbol is IMethodSymbol method &&
                            method.ContainingNamespace?.ToString().StartsWith("System.Linq") == true)
                        {
                            var diagnostic = Diagnostic.Create(
                                DiagnosticRules.HotPathLinq,
                                invocation.GetLocation(),
                                methodName, invokedMethodName);
                            context.ReportDiagnostic(diagnostic);
                        }
                    }
                }
            }

            // 3. Check string concatenation (interpolation and Format).
            //    Skip logging calls and exception constructors — those are one-time error paths.
            foreach (var interpolation in methodDecl.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>())
            {
                if (IsLogOrExceptionArgument(interpolation)) continue;
                var diagnostic = Diagnostic.Create(
                    DiagnosticRules.HotPathStringConcat,
                    interpolation.GetLocation(),
                    methodName);
                context.ReportDiagnostic(diagnostic);
            }

            foreach (var invocation in methodDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is MemberAccessExpressionSyntax ma &&
                    ma.Name.Identifier.Text == "Format")
                {
                    var symbolInfo = context.SemanticModel.GetSymbolInfo(ma);
                    if (symbolInfo.Symbol is IMethodSymbol m &&
                        m.ContainingType?.ToString() == "string")
                    {
                        if (IsLogOrExceptionArgument(invocation)) continue;
                        var diagnostic = Diagnostic.Create(
                            DiagnosticRules.HotPathStringConcat,
                            invocation.GetLocation(),
                            methodName);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }

        // ── Zero-allocation enumerator types ──────────────────────────────

        private static bool IsZeroAllocationEnumerator(string fullTypeName)
        {
            return fullTypeName.StartsWith("System.Collections.Generic.List<") ||
                   fullTypeName.EndsWith("[]") ||
                   fullTypeName.StartsWith("Unity.Collections.NativeArray<") ||
                   fullTypeName.StartsWith("Unity.Collections.NativeList<") ||
                   fullTypeName.StartsWith("System.Span<") ||
                   fullTypeName.StartsWith("System.ReadOnlySpan<") ||
                   fullTypeName.StartsWith("System.Collections.Immutable.ImmutableArray<") ||
                   fullTypeName.StartsWith("System.Collections.Generic.Dictionary<");
        }

        // ── Logging / exception detection ─────────────────────────────────

        private static bool IsLogOrExceptionArgument(ExpressionSyntax node)
        {
            // Check if this interpolation/format is a direct argument to a log method
            if (node.Parent is ArgumentSyntax arg &&
                arg.Parent is ArgumentListSyntax argList &&
                argList.Parent is InvocationExpressionSyntax invoc)
            {
                var exprText = invoc.Expression.ToString();
                // Unity Debug.Log* / CLogger.Log* / any *Log* / *Error* calls
                if (exprText.Contains("Debug.Log") || exprText.Contains("Debug.LogWarning") ||
                    exprText.Contains("Debug.LogError") || exprText.Contains("CLogger.Log") ||
                    exprText.Contains("LogInfo") || exprText.Contains("LogWarning") ||
                    exprText.Contains("LogError") || exprText.Contains("LogDebug"))
                    return true;
            }
            // Check if this is inside a throw expression
            if (node.Ancestors().OfType<ThrowExpressionSyntax>().Any()) return true;
            if (node.Ancestors().OfType<ThrowStatementSyntax>().Any()) return true;

            return false;
        }

        private static bool IsHotPathMethod(string methodName)
        {
            foreach (var name in HotPathMethodNames)
            {
                if (methodName == name) return true;
            }
            return false;
        }
    }
}
