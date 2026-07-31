using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Keeps category ownership and channel construction at one discoverable boundary per
    /// governed package assembly, including copyable samples and benchmarks. Consumers use
    /// that assembly's facade instead of creating ad hoc channels throughout implementation files.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ModuleLogConventionAnalyzer : DiagnosticAnalyzer
    {
        private const string LogChannelMetadataName = "CycloneGames.Logging.LogChannel";
        private const string LogWriterMetadataName = "CycloneGames.Logging.ILogWriter";
        private const string FacadeSuffix = "Log";
        private const string DiagnosticsPathSegment = "/Diagnostics/";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DiagnosticRules.ModuleLogConvention);

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

            INamedTypeSymbol? logChannel =
                context.Compilation.GetTypeByMetadataName(LogChannelMetadataName);
            INamedTypeSymbol? logWriter =
                context.Compilation.GetTypeByMetadataName(LogWriterMetadataName);
            if (logChannel == null || logWriter == null)
            {
                return;
            }

            context.RegisterOperationAction(
                operationContext => AnalyzeInvocation(
                    operationContext,
                    analyzedTrees,
                    logChannel,
                    logWriter),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            ImmutableHashSet<SyntaxTree> analyzedTrees,
            INamedTypeSymbol logChannel,
            INamedTypeSymbol logWriter)
        {
            if (context.Operation is not IInvocationOperation invocation ||
                !analyzedTrees.Contains(invocation.Syntax.SyntaxTree))
            {
                return;
            }

            IMethodSymbol method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
            if (!string.Equals(method.Name, "Create", StringComparison.Ordinal) ||
                !SymbolEqualityComparer.Default.Equals(method.ContainingType, logChannel))
            {
                return;
            }

            INamedTypeSymbol? containingType = context.ContainingSymbol?.ContainingType;
            if (IsValidFacade(
                    containingType,
                    invocation.Syntax.SyntaxTree.FilePath,
                    logChannel,
                    logWriter))
            {
                return;
            }

            string ownerName = containingType?.ToDisplayString() ?? "<global scope>";
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticRules.ModuleLogConvention,
                invocation.Syntax.GetLocation(),
                ownerName));
        }

        private static bool IsValidFacade(
            INamedTypeSymbol? type,
            string? filePath,
            INamedTypeSymbol logChannel,
            INamedTypeSymbol logWriter)
        {
            if (type == null ||
                type.ContainingType != null ||
                !type.IsStatic ||
                type.DeclaredAccessibility != Accessibility.Internal ||
                !type.Name.EndsWith(FacadeSuffix, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            string normalizedPath = filePath!.Replace('\\', '/');
            if (normalizedPath.IndexOf(
                    DiagnosticsPathSegment,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            int fileNameStart = normalizedPath.LastIndexOf('/') + 1;
            string expectedFileName = type.Name + ".cs";
            if (!string.Equals(
                normalizedPath.Substring(fileNameStart),
                expectedFileName,
                StringComparison.Ordinal))
            {
                return false;
            }

            bool hasCategory = type.GetMembers("Category")
                .OfType<IFieldSymbol>()
                .Any(field =>
                    field.DeclaredAccessibility == Accessibility.Internal &&
                    field.IsConst &&
                    field.Type.SpecialType == SpecialType.System_String);

            bool hasChannel = type.GetMembers("Channel")
                .OfType<IFieldSymbol>()
                .Any(field =>
                    field.DeclaredAccessibility == Accessibility.Internal &&
                    field.IsStatic &&
                    field.IsReadOnly &&
                    SymbolEqualityComparer.Default.Equals(field.Type, logChannel));

            bool hasFactory = type.GetMembers("Create")
                .OfType<IMethodSymbol>()
                .Any(method =>
                    method.MethodKind == MethodKind.Ordinary &&
                    method.DeclaredAccessibility == Accessibility.Internal &&
                    method.IsStatic &&
                    method.Arity == 0 &&
                    SymbolEqualityComparer.Default.Equals(method.ReturnType, logChannel) &&
                    method.Parameters.Length == 1 &&
                    method.Parameters[0].RefKind == RefKind.None &&
                    string.Equals(method.Parameters[0].Name, "logWriter", StringComparison.Ordinal) &&
                    SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, logWriter));

            return hasCategory && hasChannel && hasFactory;
        }
    }
}
