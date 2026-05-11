using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CycloneGames.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CameraMainCachingAnalyzer : DiagnosticAnalyzer
    {
        private static readonly string[] HotPathMethodNames =
        {
            "Update", "LateUpdate", "FixedUpdate",
            "OnTick", "Tick", "PreUpdate", "PostUpdate",
            "OnUpdate", "OnPreTick", "OnPostTick",
            "OnGUI"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DiagnosticRules.CameraMainInHotPath);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        }

        private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;
            if (memberAccess.Name.Identifier.Text != "main") return;

            var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess);
            if (symbolInfo.Symbol is not IPropertySymbol prop) return;
            if (prop.ContainingType?.ToString() != "UnityEngine.Camera" || prop.Name != "main") return;

            var containingMethod = memberAccess.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (containingMethod == null) return;

            var methodName = containingMethod.Identifier.Text;
            if (!IsHotPathMethod(methodName)) return;

            var diagnostic = Diagnostic.Create(
                DiagnosticRules.CameraMainInHotPath,
                memberAccess.GetLocation(),
                methodName);
            context.ReportDiagnostic(diagnostic);
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
