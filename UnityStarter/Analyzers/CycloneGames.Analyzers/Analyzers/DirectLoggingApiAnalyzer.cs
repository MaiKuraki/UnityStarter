using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Prevents CycloneGames package assemblies from bypassing the shared logging contract.
    /// Exact logging backend assemblies and test, tool, and code generation boundaries are outside this
    /// rule's scope by design; copyable samples and benchmarks remain governed.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DirectLoggingApiAnalyzer : DiagnosticAnalyzer
    {
        private const string UnityDebugMetadataName = "UnityEngine.Debug";
        private const string UnityMonoBehaviourMetadataName = "UnityEngine.MonoBehaviour";
        private const string SystemConsoleMetadataName = "System.Console";
        private const string BackendLogPipelineMetadataName = "CycloneGames.Logging.Pipeline.LogPipeline";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DiagnosticRules.DirectLoggingApi);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            if (!LoggingAnalyzerScope.IsEnforcedAssembly(context.Compilation.AssemblyName))
            {
                return;
            }

            var analyzedTrees = context.Compilation.SyntaxTrees
                .Where(tree => !LoggingAnalyzerScope.IsExemptPath(tree.FilePath))
                .ToImmutableHashSet();

            if (analyzedTrees.Count == 0)
            {
                return;
            }

            var knownTypes = new KnownTypes(context.Compilation);

            context.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, analyzedTrees, knownTypes),
                OperationKind.Invocation);

            context.RegisterOperationAction(
                operationContext => AnalyzePropertyReference(operationContext, analyzedTrees, knownTypes),
                OperationKind.PropertyReference);

            if (knownTypes.BackendLogPipeline != null &&
                !LoggingAnalyzerScope.MayReferenceBackendPipeline(context.Compilation.AssemblyName))
            {
                context.RegisterSyntaxNodeAction(
                    syntaxContext => AnalyzeBackendLogPipelineTypeUse(
                        syntaxContext,
                        analyzedTrees,
                        knownTypes.BackendLogPipeline),
                    SyntaxKind.IdentifierName);
            }
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            ImmutableHashSet<SyntaxTree> analyzedTrees,
            KnownTypes knownTypes)
        {
            if (!analyzedTrees.Contains(context.Operation.Syntax.SyntaxTree) ||
                context.Operation is not IInvocationOperation invocation)
            {
                return;
            }

            IMethodSymbol method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
            INamedTypeSymbol? containingType = method.ContainingType;

            if (knownTypes.UnityDebug != null &&
                SymbolEqualityComparer.Default.Equals(containingType, knownTypes.UnityDebug) &&
                IsUnityDebugOutputMethod(method.Name))
            {
                Report(context, invocation.Syntax.GetLocation(), UnityDebugMetadataName + "." + method.Name);
                return;
            }

            if (knownTypes.UnityMonoBehaviour != null &&
                SymbolEqualityComparer.Default.Equals(containingType, knownTypes.UnityMonoBehaviour) &&
                string.Equals(method.Name, "print", StringComparison.Ordinal))
            {
                Report(context, invocation.Syntax.GetLocation(), UnityMonoBehaviourMetadataName + ".print");
                return;
            }

            if (knownTypes.SystemConsole != null &&
                SymbolEqualityComparer.Default.Equals(containingType, knownTypes.SystemConsole) &&
                method.Name.StartsWith("Write", StringComparison.Ordinal))
            {
                Report(context, invocation.Syntax.GetLocation(), SystemConsoleMetadataName + "." + method.Name);
            }
        }

        private static void AnalyzePropertyReference(
            OperationAnalysisContext context,
            ImmutableHashSet<SyntaxTree> analyzedTrees,
            KnownTypes knownTypes)
        {
            if (!analyzedTrees.Contains(context.Operation.Syntax.SyntaxTree) ||
                context.Operation is not IPropertyReferenceOperation propertyReference)
            {
                return;
            }

            IPropertySymbol property = propertyReference.Property;
            if (knownTypes.SystemConsole != null &&
                SymbolEqualityComparer.Default.Equals(property.ContainingType, knownTypes.SystemConsole) &&
                (string.Equals(property.Name, "Out", StringComparison.Ordinal) ||
                 string.Equals(property.Name, "Error", StringComparison.Ordinal)))
            {
                Report(context, propertyReference.Syntax.GetLocation(), SystemConsoleMetadataName + "." + property.Name);
                return;
            }

            if (knownTypes.UnityDebug != null &&
                SymbolEqualityComparer.Default.Equals(property.ContainingType, knownTypes.UnityDebug) &&
                string.Equals(property.Name, "unityLogger", StringComparison.Ordinal))
            {
                Report(context, propertyReference.Syntax.GetLocation(), UnityDebugMetadataName + ".unityLogger");
            }
        }

        private static void AnalyzeBackendLogPipelineTypeUse(
            SyntaxNodeAnalysisContext context,
            ImmutableHashSet<SyntaxTree> analyzedTrees,
            INamedTypeSymbol backendLogPipeline)
        {
            if (!analyzedTrees.Contains(context.Node.SyntaxTree) ||
                context.Node is not IdentifierNameSyntax identifier ||
                identifier.IsPartOfStructuredTrivia() ||
                IsInsideUsingDirective(identifier))
            {
                return;
            }

            IAliasSymbol? alias = context.SemanticModel.GetAliasInfo(identifier, context.CancellationToken);
            ISymbol? symbol = alias?.Target ??
                              context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;

            if (!SymbolEqualityComparer.Default.Equals(symbol, backendLogPipeline))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticRules.DirectLoggingApi,
                identifier.GetLocation(),
                BackendLogPipelineMetadataName));
        }

        private static bool IsInsideUsingDirective(SyntaxNode node)
        {
            for (SyntaxNode? current = node.Parent; current != null; current = current.Parent)
            {
                if (current is UsingDirectiveSyntax)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUnityDebugOutputMethod(string methodName)
        {
            return methodName.StartsWith("Log", StringComparison.Ordinal) ||
                   methodName.StartsWith("Assert", StringComparison.Ordinal);
        }

        private static void Report(OperationAnalysisContext context, Location location, string apiName)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticRules.DirectLoggingApi,
                location,
                apiName));
        }

        private sealed class KnownTypes
        {
            internal KnownTypes(Compilation compilation)
            {
                UnityDebug = compilation.GetTypeByMetadataName(UnityDebugMetadataName);
                UnityMonoBehaviour = compilation.GetTypeByMetadataName(UnityMonoBehaviourMetadataName);
                SystemConsole = compilation.GetTypeByMetadataName(SystemConsoleMetadataName);
                BackendLogPipeline = compilation.GetTypeByMetadataName(BackendLogPipelineMetadataName);
            }

            internal INamedTypeSymbol? UnityDebug { get; }
            internal INamedTypeSymbol? UnityMonoBehaviour { get; }
            internal INamedTypeSymbol? SystemConsole { get; }
            internal INamedTypeSymbol? BackendLogPipeline { get; }
        }
    }
}
