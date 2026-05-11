using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Detects forbidden Unity APIs (GameObject.Find, FindObjectOfType, SendMessage,
    /// Invoke, Resources.Load) in production Runtime code.
    ///
    /// Lazy-init caching pattern (null-check field, assign result, return cached)
    /// is downgraded from Error to Info. It is acceptable singleton resolution but
    /// DI or [SerializeField] references are preferred.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ForbiddenUnityApiAnalyzer : DiagnosticAnalyzer
    {
        private static readonly string[] HotPathMethods =
        {
            "Update", "LateUpdate", "FixedUpdate",
            "OnTick", "Tick", "PreUpdate", "PostUpdate",
            "OnUpdate", "Awake"
        };

        // Methods that scan the entire scene (all equally problematic).
        private static readonly string[] SceneScanMethods =
        {
            "Find",                 // GameObject.Find
            "FindObjectOfType",
            "FindObjectsOfType",
            "FindObjectByType",
            "FindObjectsByType",
            "FindFirstObjectByType",
            "FindAnyObjectByType"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            DiagnosticRules.GameObjectFind,
            DiagnosticRules.FindObjectOfType,
            DiagnosticRules.SendMessageApi,
            DiagnosticRules.InvokeApi,
            DiagnosticRules.ResourcesLoad);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // GameObject.Find / FindObjectOfType / SendMessage / Invoke
            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

            // Resources.Load
            context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        }

        private static bool IsLazyInitCachePattern(InvocationExpressionSyntax invocation)
        {
            // Pattern: result is assigned to a field, and the containing method
            // has a null-check guard on that field before the call.
            //
            // Example (acceptable):
            //   if (_uiRoot == null) { _uiRoot = FindFirstObjectByType<UIRoot>(); }
            //   return _uiRoot;
            //
            // Example (unacceptable):
            //   var enemies = FindObjectsByType<Enemy>();  // no caching

            // Walk up through assignment, optional if statement, and method nodes.
            if (invocation.Parent is not EqualsValueClauseSyntax equalsValueClause) return false;
            if (equalsValueClause.Parent is not AssignmentExpressionSyntax assignment) return false;

            // Check left side of assignment is a field access
            var leftSide = assignment.Left;
            if (leftSide is not IdentifierNameSyntax ident &&
                leftSide is not MemberAccessExpressionSyntax) return false;

            // Check if we're inside an if-statement whose condition checks the same field
            var ifStmt = assignment.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault();
            if (ifStmt == null) return false;

            var condition = ifStmt.Condition;

            // Simple pattern: if (field == null) or if (field is null)
            if (condition is BinaryExpressionSyntax binaryExpr &&
                binaryExpr.Kind() == SyntaxKind.EqualsExpression)
            {
                var leftNull = binaryExpr.Left is LiteralExpressionSyntax l &&
                               l.Kind() == SyntaxKind.NullLiteralExpression;
                var rightNull = binaryExpr.Right is LiteralExpressionSyntax r &&
                                r.Kind() == SyntaxKind.NullLiteralExpression;
                if (leftNull || rightNull)
                {
                    var otherSide = leftNull ? binaryExpr.Right : binaryExpr.Left;
                    return otherSide.ToString() == leftSide.ToString();
                }
            }

            // Pattern: if (field is null)
            if (condition is IsPatternExpressionSyntax isPattern &&
                isPattern.Pattern is ConstantPatternSyntax constantPattern &&
                constantPattern.Expression is LiteralExpressionSyntax lit &&
                lit.Kind() == SyntaxKind.NullLiteralExpression)
            {
                return isPattern.Expression.ToString() == leftSide.ToString();
            }

            return false;
        }

        private static bool IsInHotPathMethod(InvocationExpressionSyntax invocation)
        {
            var method = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method == null) return false;
            foreach (var name in HotPathMethods)
            {
                if (method.Identifier.Text == name) return true;
            }
            return false;
        }

        private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
            var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
            if (methodSymbol == null) return;

            var containingType = methodSymbol.ContainingType?.ToString();

            // Scene scan APIs.
            if (containingType == "UnityEngine.Object" || containingType == "UnityEngine.GameObject")
            {
                if (methodSymbol.Name == "Find" && containingType == "UnityEngine.GameObject")
                {
                    ReportWithCacheAwareness(context, invocation,
                        DiagnosticRules.GameObjectFind,
                        invocation.ArgumentList.Arguments.Count > 0
                            ? invocation.ArgumentList.Arguments[0].ToString() : "?");
                    return;
                }

                foreach (var scanName in SceneScanMethods)
                {
                    if (methodSymbol.Name == scanName)
                    {
                        ReportWithCacheAwareness(context, invocation,
                            DiagnosticRules.FindObjectOfType,
                            methodSymbol.Name);
                        return;
                    }
                }
            }

            // String-based message APIs.
            if (containingType == "UnityEngine.Component" || containingType == "UnityEngine.GameObject")
            {
                if (methodSymbol.Name == "SendMessage" ||
                    methodSymbol.Name == "BroadcastMessage" ||
                    methodSymbol.Name == "SendMessageUpwards")
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticRules.SendMessageApi,
                        invocation.GetLocation(),
                        methodSymbol.Name));
                }
            }

            // String-based MonoBehaviour timer APIs.
            if (containingType == "UnityEngine.MonoBehaviour")
            {
                if (methodSymbol.Name == "Invoke" ||
                    methodSymbol.Name == "InvokeRepeating" ||
                    methodSymbol.Name == "CancelInvoke" ||
                    methodSymbol.Name == "IsInvoking")
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticRules.InvokeApi,
                        invocation.GetLocation(),
                        methodSymbol.Name));
                }
            }
        }

        /// <summary>
        /// Reports the diagnostic, downgrading severity to Info if the call
        /// uses a lazy-init caching pattern.
        /// </summary>
        private static void ReportWithCacheAwareness(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax invocation,
            DiagnosticDescriptor rule,
            string detail)
        {
            var isCached = IsLazyInitCachePattern(invocation);
            var isInHotPath = IsInHotPathMethod(invocation);

            // Cached lazy-init outside hot paths: Info (suggest DI, don't block build)
            if (isCached && !isInHotPath)
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticRules.FindObjectOfType,
                    invocation.GetLocation(),
                    DiagnosticSeverity.Info,
                    null,
                    null,
                    new(string, string)[]
                    {
                        new("CachedPattern", "true")
                    });
                context.ReportDiagnostic(diagnostic);
                return;
            }

            // Uncached or in hot path: full severity
            var sevDiagnostic = Diagnostic.Create(rule, invocation.GetLocation(), detail);
            context.ReportDiagnostic(sevDiagnostic);
        }

        private void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;

            if (memberAccess.Name.Identifier.Text.StartsWith("Load"))
            {
                var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess);
                if (symbolInfo.Symbol is IMethodSymbol method &&
                    method.ContainingType?.ToString() == "UnityEngine.Resources")
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticRules.ResourcesLoad,
                        memberAccess.GetLocation()));
                }
            }
        }
    }
}
