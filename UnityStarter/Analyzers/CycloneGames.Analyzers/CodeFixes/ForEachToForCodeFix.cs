using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace CycloneGames.Analyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ForEachToForCodeFix)), Shared]
    public class ForEachToForCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(DiagnosticIds.HotPathForEach);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            if (root == null) return;

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var forEachStmt = root.FindToken(diagnosticSpan.Start).Parent?
                .AncestorsAndSelf().OfType<ForEachStatementSyntax>().FirstOrDefault();
            if (forEachStmt == null) return;

            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);
            if (semanticModel == null) return;
            if (GetIndexedAccessKind(semanticModel, forEachStmt) == null) return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Convert foreach to for",
                    c => ConvertToForAsync(context.Document, forEachStmt, c),
                    equivalenceKey: nameof(ForEachToForCodeFix)),
                diagnostic);
        }

        private static async Task<Document> ConvertToForAsync(
            Document document,
            ForEachStatementSyntax forEachStmt,
            CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null) return document;

            var collectionExpr = forEachStmt.Expression;
            var itemName = forEachStmt.Identifier.Text;
            var countOrLength = GetIndexedAccessKind(semanticModel, forEachStmt);
            if (countOrLength == null) return document;

            var indexName = GetAvailableIndexName(forEachStmt);
            var forDecl = SyntaxFactory.VariableDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(indexName)
                        .WithInitializer(SyntaxFactory.EqualsValueClause(
                            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                                SyntaxFactory.Literal(0))))));
            var forCondition = SyntaxFactory.BinaryExpression(
                SyntaxKind.LessThanExpression,
                SyntaxFactory.IdentifierName(indexName),
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    collectionExpr,
                    SyntaxFactory.IdentifierName(countOrLength)));
            var forIncrement = SyntaxFactory.PostfixUnaryExpression(
                SyntaxKind.PostIncrementExpression,
                SyntaxFactory.IdentifierName(indexName));
            var forStmt = SyntaxFactory.ForStatement(
                forDecl,
                default,
                forCondition,
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(forIncrement),
                ReplaceIdentifierInStatement(
                    semanticModel,
                    forEachStmt,
                    itemName,
                    indexName,
                    collectionExpr));

            editor.ReplaceNode(forEachStmt, forStmt);
            return editor.GetChangedDocument();
        }

        private static StatementSyntax ReplaceIdentifierInStatement(
            SemanticModel semanticModel,
            ForEachStatementSyntax forEachStmt,
            string itemName,
            string indexName,
            ExpressionSyntax collectionExpr)
        {
            var statement = forEachStmt.Statement;
            var foreachSymbol = semanticModel.GetDeclaredSymbol(forEachStmt);
            var collectionAccess = SyntaxFactory.ElementAccessExpression(
                collectionExpr,
                SyntaxFactory.BracketedArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(indexName)))));

            return statement.ReplaceNodes(
                statement.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Where(id =>
                        id.Identifier.Text == itemName &&
                        SymbolEqualityComparer.Default.Equals(
                            semanticModel.GetSymbolInfo(id).Symbol,
                            foreachSymbol)),
                (old, _) => collectionAccess);
        }

        private static string? GetIndexedAccessKind(SemanticModel semanticModel, ForEachStatementSyntax forEachStmt)
        {
            var type = semanticModel.GetTypeInfo(forEachStmt.Expression).Type;
            if (type == null) return null;
            if (type is IArrayTypeSymbol) return "Length";

            var hasCount = HasReadableIntProperty(type, "Count");
            var hasIndexer = HasIntIndexer(type);
            if (hasCount && hasIndexer) return "Count";

            var hasLength = HasReadableIntProperty(type, "Length");
            return hasLength && hasIndexer ? "Length" : null;
        }

        private static bool HasReadableIntProperty(ITypeSymbol type, string propertyName)
        {
            foreach (var member in type.GetMembers(propertyName).OfType<IPropertySymbol>())
            {
                if (member.GetMethod != null && member.Type.SpecialType == SpecialType.System_Int32)
                    return true;
            }

            return false;
        }

        private static bool HasIntIndexer(ITypeSymbol type)
        {
            foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (!member.IsIndexer || member.Parameters.Length != 1) continue;
                if (member.Parameters[0].Type.SpecialType == SpecialType.System_Int32)
                    return true;
            }

            return false;
        }

        private static string GetAvailableIndexName(ForEachStatementSyntax forEachStmt)
        {
            var usedNames = forEachStmt.Statement.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Select(id => id.Identifier.Text)
                .ToImmutableHashSet();

            if (!usedNames.Contains("i")) return "i";
            if (!usedNames.Contains("index")) return "index";

            var suffix = 0;
            while (usedNames.Contains("index" + suffix))
                suffix++;

            return "index" + suffix;
        }
    }
}
