using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Detects circular method-call dependencies between static classes.
    /// A cycle like A -> B -> C -> A causes <c>TypeInitializationException</c> at
    /// unpredictable times depending on which class is accessed first.
    ///
    /// Algorithm: build directed call graph, use DFS with recursion-stack tracking
    /// to find all cycles, deduplicate canonical forms, and report each invocation
    /// in each unique cycle at its exact source location.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class StaticClassCircularDependencyAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DiagnosticRules.StaticClassCircularDependency);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            // Graph: dict[caller][callee] = list of invocation sites
            var callGraph = new ConcurrentDictionary<string, ConcurrentDictionary<string, List<InvocationExpressionSyntax>>>();

            context.RegisterSyntaxNodeAction(
                nodeCtx => CollectCall(nodeCtx, callGraph),
                SyntaxKind.InvocationExpression);

            context.RegisterCompilationEndAction(
                endCtx => DetectCycles(endCtx, callGraph));
        }

        private static void CollectCall(
            SyntaxNodeAnalysisContext ctx,
            ConcurrentDictionary<string, ConcurrentDictionary<string, List<InvocationExpressionSyntax>>> callGraph)
        {
            if (ctx.Node is not InvocationExpressionSyntax invocation) return;
            if (ctx.ContainingSymbol?.ContainingType is not INamedTypeSymbol callerType) return;
            if (!callerType.IsStatic) return;

            var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol calleeMethod) return;
            if (!calleeMethod.ContainingType.IsStatic) return;

            var callerName = callerType.ToString();
            var calleeName = calleeMethod.ContainingType.ToString();

            // Self-call is not a dependency edge.
            if (callerName == calleeName) return;

            var calleeMap = callGraph.GetOrAdd(callerName, _ => new ConcurrentDictionary<string, List<InvocationExpressionSyntax>>());
            var sites = calleeMap.GetOrAdd(calleeName, _ => new List<InvocationExpressionSyntax>());

            // InvocationExpressionSyntax is not immutable but we only add during
            // the syntax-walk phase (before CompilationEnd), so no lock needed.
            lock (sites) { sites.Add(invocation); }
        }

        private static void DetectCycles(
            CompilationAnalysisContext ctx,
            ConcurrentDictionary<string, ConcurrentDictionary<string, List<InvocationExpressionSyntax>>> callGraph)
        {
            var allNodes = new HashSet<string>();
            foreach (var kv in callGraph)
            {
                allNodes.Add(kv.Key);
                foreach (var callee in kv.Value.Keys)
                    allNodes.Add(callee);
            }

            // visited = fully processed (black)
            // inStack  = currently on recursion stack (gray)
            var visited = new HashSet<string>();
            var inStack = new HashSet<string>();
            var path = new List<string>();
            var foundCycles = new HashSet<string>(); // canonical-form dedup

            foreach (var node in allNodes)
            {
                if (!visited.Contains(node))
                    Dfs(node, callGraph, visited, inStack, path, foundCycles, ctx);
            }
        }

        private static void Dfs(
            string current,
            ConcurrentDictionary<string, ConcurrentDictionary<string, List<InvocationExpressionSyntax>>> callGraph,
            HashSet<string> visited,
            HashSet<string> inStack,
            List<string> path,
            HashSet<string> foundCycles,
            CompilationAnalysisContext ctx)
        {
            if (inStack.Contains(current))
            {
                // Found a cycle: path[cycleStart .. end] + current
                int startIdx = path.IndexOf(current);
                var rawCycle = path.Skip(startIdx).ToList();
                rawCycle.Add(current); // close the loop

                var canonical = CanonicalForm(rawCycle);
                if (foundCycles.Add(canonical))
                    ReportCycle(canonical, rawCycle, callGraph, ctx);

                return;
            }

            if (visited.Contains(current)) return;

            visited.Add(current);
            inStack.Add(current);
            path.Add(current);

            if (callGraph.TryGetValue(current, out var callees))
            {
                foreach (var callee in callees.Keys)
                    Dfs(callee, callGraph, visited, inStack, path, foundCycles, ctx);
            }

            path.RemoveAt(path.Count - 1);
            inStack.Remove(current);
        }

        private static string CanonicalForm(List<string> cycle)
        {
            if (cycle.Count <= 1) return string.Join(" -> ", cycle);

            // Find the lexicographically smallest starting position
            int bestStart = 0;
            for (int i = 1; i < cycle.Count; i++)
            {
                if (string.CompareOrdinal(cycle[i], cycle[bestStart]) < 0)
                    bestStart = i;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < cycle.Count; i++)
            {
                if (i > 0) sb.Append(" -> ");
                sb.Append(cycle[(bestStart + i) % cycle.Count]);
            }
            return sb.ToString();
        }

        private static void ReportCycle(
            string canonical,
            List<string> rawCycle,
            ConcurrentDictionary<string, ConcurrentDictionary<string, List<InvocationExpressionSyntax>>> callGraph,
            CompilationAnalysisContext ctx)
        {
            // Build a readable cycle description for the message
            var cycleDesc = new StringBuilder();
            for (int i = 0; i < rawCycle.Count; i++)
            {
                if (i > 0) cycleDesc.Append(" -> ");
                cycleDesc.Append(rawCycle[i]);
            }

            for (int i = 0; i < rawCycle.Count - 1; i++)
            {
                var caller = rawCycle[i];
                var callee = rawCycle[i + 1];

                if (callGraph.TryGetValue(caller, out var calleeMap) &&
                    calleeMap.TryGetValue(callee, out var sites))
                {
                    foreach (var site in sites)
                    {
                        var diagnostic = Diagnostic.Create(
                            DiagnosticRules.StaticClassCircularDependency,
                            site.GetLocation(),
                            caller, cycleDesc.ToString());
                        ctx.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }
}
