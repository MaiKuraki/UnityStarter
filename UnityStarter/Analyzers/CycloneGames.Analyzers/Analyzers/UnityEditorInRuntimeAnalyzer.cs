using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Detects <c>UnityEditor</c> namespace usage in files outside Editor/Test/Sample folders.
    /// Skips code inside <c>#if UNITY_EDITOR</c> preprocessor blocks. Those are correctly
    /// stripped from production builds and are safe.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class UnityEditorInRuntimeAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DiagnosticRules.UnityEditorInRuntime);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeUsing, SyntaxKind.UsingDirective);
            context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        }

        private static bool IsAllowedDirectory(string filePath)
        {
            var normalized = filePath.Replace('\\', '/');
            return normalized.Contains("/Editor/") ||
                   normalized.Contains("/Samples/") ||
                   normalized.Contains("/Sample/") ||
                   normalized.Contains("/Tests/");
        }

        /// <summary>
        /// Checks whether the given syntax node is inside an #if UNITY_EDITOR preprocessor block.
        /// Code inside such blocks is stripped from non-Editor builds and is safe.
        /// Also handles the entire-file #if UNITY_EDITOR ... #endif pattern.
        /// </summary>
        private static bool IsInsideUnityEditorDirective(SyntaxNode node)
        {
            var directive = node.GetLeadingTrivia()
                .Select(t => t.GetStructure())
                .OfType<IfDirectiveTriviaSyntax>()
                .FirstOrDefault();

            // Check leading trivia for #if UNITY_EDITOR
            var triviaList = node.GetLeadingTrivia();
            foreach (var trivia in triviaList)
            {
                if (trivia.GetStructure() is IfDirectiveTriviaSyntax ifDirective)
                {
                    if (ifDirective.Condition.ToString().Contains("UNITY_EDITOR"))
                        return true;
                }
            }

            // Also check the using directive's parent trivia context
            var parentTrivia = node.Parent?.GetLeadingTrivia() ?? node.GetLeadingTrivia();
            foreach (var trivia in parentTrivia)
            {
                if (trivia.GetStructure() is IfDirectiveTriviaSyntax ifDirective)
                {
                    if (ifDirective.Condition.ToString().Contains("UNITY_EDITOR"))
                        return true;
                }
            }

            // Check all ancestor trivia for #if UNITY_EDITOR
            // Some files wrap the entire content in #if UNITY_EDITOR
            var root = node.SyntaxTree.GetRoot();
            var allDirectives = root.DescendantTrivia()
                .Select(t => t.GetStructure())
                .OfType<IfDirectiveTriviaSyntax>()
                .Where(d => d.Condition.ToString().Contains("UNITY_EDITOR"));

            var nodeSpan = node.Span;
            foreach (var ifDir in allDirectives)
            {
                // Find the matching #endif
                var related = ifDir.GetRelatedDirectives();
                var endIf = related.FirstOrDefault(d =>
                    d.Kind() == SyntaxKind.EndIfDirectiveTrivia);

                if (endIf != null &&
                    nodeSpan.Start >= ifDir.SpanStart &&
                    nodeSpan.End <= endIf.SpanStart)
                {
                    return true;
                }
            }

            return false;
        }

        private void AnalyzeUsing(SyntaxNodeAnalysisContext context)
        {
            var usingDirective = (UsingDirectiveSyntax)context.Node;
            var name = usingDirective.Name?.ToString() ?? "";

            if (!name.StartsWith("UnityEditor")) return;
            if (IsAllowedDirectory(usingDirective.SyntaxTree.FilePath)) return;
            if (IsInsideUnityEditorDirective(usingDirective)) return;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticRules.UnityEditorInRuntime,
                usingDirective.GetLocation(),
                name));
        }

        private void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;
            var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess);
            if (symbolInfo.Symbol == null) return;

            var ns = symbolInfo.Symbol.ContainingNamespace?.ToString() ?? "";
            if (!ns.StartsWith("UnityEditor")) return;
            if (IsAllowedDirectory(memberAccess.SyntaxTree.FilePath)) return;
            if (IsInsideUnityEditorDirective(memberAccess)) return;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticRules.UnityEditorInRuntime,
                memberAccess.GetLocation(),
                ns));
        }
    }
}
