using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Detects <c>component.transform.position</c> chain access on
    /// MonoBehaviour/Component references in hot path methods.
    /// Each dot in the chain is a native interop call; caching the Transform
    /// reference eliminates half of them.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class TransformChainAccessAnalyzer : DiagnosticAnalyzer
    {
        private static readonly string[] HotPathMethods =
        {
            "Update", "LateUpdate", "FixedUpdate",
            "OnTick", "Tick", "PreUpdate", "PostUpdate",
            "OnUpdate"
        };

        // Unity types whose .transform access we track
        private static readonly string[] ComponentTypes =
        {
            "UnityEngine.MonoBehaviour",
            "UnityEngine.Component",
            "UnityEngine.AudioListener",
            "UnityEngine.AudioSource",
            "UnityEngine.Camera",
            "UnityEngine.Collider",
            "UnityEngine.Rigidbody",
            "UnityEngine.Rigidbody2D",
            "UnityEngine.Renderer",
            "UnityEngine.MeshRenderer",
            "UnityEngine.SkinnedMeshRenderer"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DiagnosticRules.TransformChainAccess);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        }

        private void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;

            // We want: something.transform . position/rotation/localPosition/etc.
            // Pattern: (ComponentRef).transform.PROPERTY
            if (memberAccess.Parent is not MemberAccessExpressionSyntax) return;

            // Check if the current access is ".transform"
            if (memberAccess.Name.Identifier.Text != "transform") return;

            // Get the type of the expression before ".transform"
            var exprType = context.SemanticModel.GetTypeInfo(memberAccess.Expression);
            if (exprType.Type == null) return;

            var typeFullName = exprType.Type.ToString();
            var isComponentType = false;
            foreach (var ct in ComponentTypes)
            {
                if (typeFullName == ct || exprType.Type.AllInterfaces.Any(
                    i => i.ToString() == ct || i.BaseType?.ToString() == ct))
                {
                    isComponentType = true;
                    break;
                }
            }
            // Also check base type chain
            if (!isComponentType)
            {
                var baseType = exprType.Type.BaseType;
                while (baseType != null)
                {
                    if (baseType.ToString() == "UnityEngine.Component" ||
                        baseType.ToString() == "UnityEngine.Behaviour" ||
                        baseType.ToString() == "UnityEngine.MonoBehaviour")
                    {
                        isComponentType = true;
                        break;
                    }
                    baseType = baseType.BaseType;
                }
            }
            if (!isComponentType) return;

            // Check we're inside a hot path method
            var containingMethod = memberAccess.Ancestors()
                .OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (containingMethod == null) return;

            var methodName = containingMethod.Identifier.Text;
            var isHotPath = false;
            foreach (var hp in HotPathMethods)
            {
                if (methodName == hp) { isHotPath = true; break; }
            }
            if (!isHotPath) return;

            var diagnostic = Diagnostic.Create(
                DiagnosticRules.TransformChainAccess,
                memberAccess.GetLocation(),
                memberAccess.Expression.ToString(), methodName);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
