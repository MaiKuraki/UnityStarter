using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CycloneGames.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class HotPathUnityBestPracticeAnalyzer : DiagnosticAnalyzer
    {
        private static readonly string[] HotPathMethodNames =
        {
            "Update", "LateUpdate", "FixedUpdate",
            "OnTick", "Tick", "PreUpdate", "PostUpdate",
            "OnUpdate", "OnPreTick", "OnPostTick",
            "OnGUI"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            DiagnosticRules.DebugLogInHotPath,
            DiagnosticRules.GetComponentInHotPath,
            DiagnosticRules.BoxingInHotPath,
            DiagnosticRules.LambdaAllocInHotPath);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.SimpleLambdaExpression);
            context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.ParenthesizedLambdaExpression);
            context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.AnonymousMethodExpression);
            context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            var methodName = GetContainingMethodName(invocation);
            if (!IsHotPathMethod(methodName)) return;

            var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol == null) return;

            var containingType = methodSymbol.ContainingType?.ToString();
            if (containingType == "UnityEngine.Debug" && methodSymbol.Name.StartsWith("Log"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticRules.DebugLogInHotPath,
                    invocation.GetLocation(),
                    methodName));
                return;
            }

            if (methodSymbol.Name == "GetComponent" ||
                methodSymbol.Name == "GetComponentInChildren" ||
                methodSymbol.Name == "GetComponentInParent")
            {
                if (!IsUnityObjectType(methodSymbol.ContainingType)) return;

                var typeName = methodSymbol.TypeArguments.Length > 0
                    ? methodSymbol.TypeArguments[0].Name
                    : "Component";

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticRules.GetComponentInHotPath,
                    invocation.GetLocation(),
                    typeName,
                    methodName));
            }
        }

        private static void AnalyzeLambda(SyntaxNodeAnalysisContext context)
        {
            var methodName = GetContainingMethodName((SyntaxNode)context.Node);
            if (!IsHotPathMethod(methodName)) return;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticRules.LambdaAllocInHotPath,
                context.Node.GetLocation(),
                methodName));
        }

        private static void AnalyzeConversion(OperationAnalysisContext context)
        {
            if (context.Operation is not IConversionOperation conversion) return;
            var methodName = context.ContainingSymbol is IMethodSymbol method ? method.Name : "";
            if (!IsHotPathMethod(methodName)) return;

            var sourceType = conversion.Operand.Type;
            var targetType = conversion.Type;
            if (sourceType == null || targetType == null) return;
            if (!sourceType.IsValueType) return;

            if (targetType.SpecialType != SpecialType.System_Object &&
                targetType.TypeKind != TypeKind.Interface)
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticRules.BoxingInHotPath,
                conversion.Syntax.GetLocation(),
                methodName));
        }

        private static string GetContainingMethodName(SyntaxNode node)
        {
            var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            return method?.Identifier.Text ?? "";
        }

        private static bool IsHotPathMethod(string methodName)
        {
            foreach (var name in HotPathMethodNames)
            {
                if (methodName == name) return true;
            }

            return false;
        }

        private static bool IsUnityObjectType(INamedTypeSymbol? type)
        {
            while (type != null)
            {
                var name = type.ToString();
                if (name == "UnityEngine.Component" ||
                    name == "UnityEngine.GameObject" ||
                    name == "UnityEngine.MonoBehaviour")
                    return true;

                type = type.BaseType;
            }

            return false;
        }
    }
}
