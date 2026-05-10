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

            var collectionExpr = forEachStmt.Expression;
            var itemType = forEachStmt.Type;
            var itemName = forEachStmt.Identifier.Text;

            // Generate: for (int i = 0; i < collection.Count; i++)
            var indexName = "i";
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
                    SyntaxFactory.IdentifierName("Count")));
            var forIncrement = SyntaxFactory.PostfixUnaryExpression(
                SyntaxKind.PostIncrementExpression,
                SyntaxFactory.IdentifierName(indexName));
            var forStmt = SyntaxFactory.ForStatement(
                forDecl,
                default,
                forCondition,
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(forIncrement),
                ReplaceIdentifierInStatement(forEachStmt.Statement, itemName, indexName, collectionExpr));

            editor.ReplaceNode(forEachStmt, forStmt);
            return editor.GetChangedDocument();
        }

        private static StatementSyntax ReplaceIdentifierInStatement(
            StatementSyntax statement, string itemName, string indexName, ExpressionSyntax collectionExpr)
        {
            // Replace variable references: itemName -> collection[indexName]
            var collectionAccess = SyntaxFactory.ElementAccessExpression(
                collectionExpr,
                SyntaxFactory.BracketedArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(indexName)))));

            return statement.ReplaceNodes(
                statement.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Where(id => id.Identifier.Text == itemName),
                (old, _) => collectionAccess);
        }
    }
}
