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

        private static readonly string[] AllowedCachingMethods =
        {
            "Awake", "Start", "OnEnable", "Initialize", "Init"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DiagnosticRules.CameraMainInHotPath);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        }

        private void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;

            // Must be Camera.main pattern
            if (memberAccess.Name.Identifier.Text != "main") return;

            var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess);
            if (symbolInfo.Symbol is IPropertySymbol prop &&
                prop.ContainingType?.ToString() == "UnityEngine.Camera" &&
                prop.Name == "main")
            {
                // Check if we're inside a hot path method
                var containingMethod = memberAccess.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (containingMethod == null) return;

                var methodName = containingMethod.Identifier.Text;

                // Skip if in a caching method (Awake/Start etc.)
                if (IsAllowedCachingMethod(methodName)) return;

                var diagnostic = Diagnostic.Create(
                    DiagnosticRules.CameraMainInHotPath,
                    memberAccess.GetLocation(),
                    methodName);
                context.ReportDiagnostic(diagnostic);
            }
        }

        private static bool IsAllowedCachingMethod(string methodName)
        {
            foreach (var name in AllowedCachingMethods)
            {
                if (methodName == name) return true;
            }
            return false;
        }
    }
}
