using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Detects <c>async void</c> methods in Runtime (non-Editor, non-Sample) code.
    ///
    /// Exception: methods used as C# event handlers (<c>+=</c> subscription) or UnityEvent
    /// callbacks are skipped — in C#, event handlers MUST return void, so <c>async void</c>
    /// is the only valid syntax for async event handlers.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class AsyncVoidAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DiagnosticRules.AsyncVoidInRuntime);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            var methodDecl = (MethodDeclarationSyntax)context.Node;

            // Must be async
            if (!methodDecl.Modifiers.Any(SyntaxKind.AsyncKeyword)) return;

            // Must return void
            if (methodDecl.ReturnType is not PredefinedTypeSyntax preType ||
                preType.Keyword.Kind() != SyntaxKind.VoidKeyword) return;

            // Skip samples, editor, tests
            var filePath = methodDecl.SyntaxTree.FilePath;
            var normalized = filePath.Replace('\\', '/');
            if (normalized.Contains("/Samples/") ||
                normalized.Contains("/Sample/") ||
                normalized.Contains("/Editor/") ||
                normalized.Contains("/Tests/")) return;

            // Skip if this method is used as a C# event handler (subscribed via +=)
            // In C#, event handlers must return void — async void is the only option.
            var methodName = methodDecl.Identifier.Text;
            if (IsEventSubscriber(methodDecl, methodName)) return;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticRules.AsyncVoidInRuntime,
                methodDecl.Identifier.GetLocation(),
                methodDecl.Identifier.Text));
        }

        /// <summary>
        /// Checks whether the method is subscribed to any C# event via +=.
        /// Pattern: searching the entire syntax tree for <c>+= MethodName</c>.
        /// </summary>
        private static bool IsEventSubscriber(MethodDeclarationSyntax methodDecl, string methodName)
        {
            var root = methodDecl.SyntaxTree.GetRoot();

            // Search for: someEvent += MethodName   or   someEvent += new EventHandler(MethodName)
            foreach (var assignment in root.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(a => a.Kind() == SyntaxKind.AddAssignmentExpression))
            {
                var rightHand = assignment.Right.ToString();
                // Direct subscription: event += MethodName
                if (rightHand == methodName) return true;
                // Delegate wrapper: event += new Action(MethodName) or event += (Action)MethodName
                if (rightHand.Contains("(" + methodName + ")") ||
                    rightHand.Contains("(" + methodName + ");"))
                    return true;
            }

            // Also check if the method is invoked as a direct callback from another method
            // Pattern: SomeMethod(MethodName) where MethodName is used as Action/Func argument
            foreach (var invocation in root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>())
            {
                foreach (var arg in invocation.ArgumentList.Arguments)
                {
                    if (arg.Expression is IdentifierNameSyntax ident &&
                        ident.Identifier.Text == methodName &&
                        !IsSameMethod(invocation, methodName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsSameMethod(InvocationExpressionSyntax invocation, string methodName)
        {
            return invocation.Expression is IdentifierNameSyntax id &&
                   id.Identifier.Text == methodName;
        }
    }
}
