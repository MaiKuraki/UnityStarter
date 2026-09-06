using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CycloneGames.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ConventionAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            DiagnosticRules.PublicFieldOnMonoBehaviour,
            DiagnosticRules.UsingStaticDirective,
            DiagnosticRules.RegionInRuntime,
            DiagnosticRules.ObsoleteInFramework);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeUsing, SyntaxKind.UsingDirective);
            context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
            context.RegisterSyntaxTreeAction(AnalyzeRegions);
        }

        private static void AnalyzeField(SyntaxNodeAnalysisContext context)
        {
            var field = (FieldDeclarationSyntax)context.Node;
            if (!AnalyzerSourceScope.IsRepositoryOwned(field.SyntaxTree)) return;
            if (!field.Modifiers.Any(SyntaxKind.PublicKeyword)) return;
            if (field.Modifiers.Any(SyntaxKind.ConstKeyword) || field.Modifiers.Any(SyntaxKind.StaticKeyword)) return;
            if (IsAllowedDirectory(field.SyntaxTree.FilePath)) return;

            // The rule targets fields whose own declaring type derives from UnityEngine.MonoBehaviour.
            // Bind the semantic containing type per variable instead of walking syntax ancestors: a
            // field declared inside a nested struct or a plain nested class must not inherit the
            // enclosing MonoBehaviour classification.
            foreach (var variable in field.Declaration.Variables)
            {
                if (context.SemanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol) return;
                if (!IsDerivedFrom(fieldSymbol.ContainingType, "UnityEngine.MonoBehaviour")) return;

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticRules.PublicFieldOnMonoBehaviour,
                    variable.Identifier.GetLocation(),
                    variable.Identifier.Text));
            }
        }

        private static void AnalyzeUsing(SyntaxNodeAnalysisContext context)
        {
            var usingDirective = (UsingDirectiveSyntax)context.Node;
            if (!AnalyzerSourceScope.IsRepositoryOwned(usingDirective.SyntaxTree)) return;
            if (usingDirective.StaticKeyword.IsKind(SyntaxKind.None)) return;
            if (IsAllowedDirectory(usingDirective.SyntaxTree.FilePath)) return;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticRules.UsingStaticDirective,
                usingDirective.GetLocation(),
                usingDirective.Name?.ToString() ?? ""));
        }

        private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
        {
            var attribute = (AttributeSyntax)context.Node;
            if (!AnalyzerSourceScope.IsRepositoryOwned(attribute.SyntaxTree)) return;
            if (!IsFrameworkPath(attribute.SyntaxTree.FilePath)) return;

            var symbol = context.SemanticModel.GetSymbolInfo(attribute).Symbol;
            if (symbol?.ContainingType?.ToString() != "System.ObsoleteAttribute") return;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticRules.ObsoleteInFramework,
                attribute.GetLocation()));
        }

        private static void AnalyzeRegions(SyntaxTreeAnalysisContext context)
        {
            var filePath = context.Tree.FilePath;
            if (!AnalyzerSourceScope.IsRepositoryOwned(context.Tree)) return;
            if (IsAllowedDirectory(filePath)) return;

            var root = context.Tree.GetRoot(context.CancellationToken);
            foreach (var trivia in root.DescendantTrivia())
            {
                if (trivia.GetStructure() is not RegionDirectiveTriviaSyntax region) continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticRules.RegionInRuntime,
                    region.GetLocation()));
            }
        }

        private static bool IsAllowedDirectory(string filePath)
        {
            var normalized = filePath.Replace('\\', '/');
            return normalized.Contains("/Editor/") ||
                   normalized.Contains("/Samples/") ||
                   normalized.Contains("/Sample/") ||
                   normalized.Contains("/Tests/");
        }

        private static bool IsFrameworkPath(string filePath)
        {
            var normalized = filePath.Replace('\\', '/');
            return normalized.Contains("/Assets/ThirdParty/CycloneGames/") ||
                   normalized.Contains("/Analyzers/CycloneGames.Analyzers/");
        }

        private static bool IsDerivedFrom(INamedTypeSymbol? type, string baseTypeName)
        {
            while (type != null)
            {
                if (type.ToString() == baseTypeName) return true;
                type = type.BaseType;
            }

            return false;
        }
    }
}
