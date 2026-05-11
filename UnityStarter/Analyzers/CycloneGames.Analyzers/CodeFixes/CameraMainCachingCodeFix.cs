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
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CameraMainCachingCodeFix)), Shared]
    public class CameraMainCachingCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(DiagnosticIds.CameraMainInHotPath);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            if (root == null) return;

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var memberAccess = root.FindToken(diagnosticSpan.Start).Parent?
                .AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
            if (memberAccess == null) return;

            // Find containing class
            var classDecl = memberAccess.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDecl == null) return;

            // Find containing method (where the violation is)
            var methodDecl = memberAccess.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDecl == null) return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Cache Camera.main in Awake",
                    c => CacheCameraMainAsync(context.Document, classDecl, methodDecl, memberAccess, c),
                    equivalenceKey: nameof(CameraMainCachingCodeFix)),
                diagnostic);
        }

        private static async Task<Document> CacheCameraMainAsync(
            Document document,
            ClassDeclarationSyntax classDecl,
            MethodDeclarationSyntax violatingMethod,
            MemberAccessExpressionSyntax cameraAccess,
            CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken);

            // Generate field name
            var fieldName = "_cachedMainCamera";

            // Generate field declaration
            var fieldDecl = SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.IdentifierName("Camera"),
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(fieldName))))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));

            // Generate Awake method if it doesn't exist
            var existingAwake = classDecl.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "Awake");
            if (existingAwake == null)
            {
                var awakeMethod = SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), "Awake")
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
                    .WithBody(SyntaxFactory.Block(
                        SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(fieldName),
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.IdentifierName("Camera"),
                                    SyntaxFactory.IdentifierName("main"))))));
                editor.AddMember(classDecl, awakeMethod);
            }
            else
            {
                var body = existingAwake.Body ?? SyntaxFactory.Block();
                var assignment = SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName(fieldName),
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("Camera"),
                            SyntaxFactory.IdentifierName("main"))));
                editor.ReplaceNode(existingAwake,
                    existingAwake.WithBody(body.AddStatements(assignment)));
            }

            // Add field
            editor.AddMember(classDecl, fieldDecl);

            // Replace Camera.main with _cachedMainCamera
            var replacement = SyntaxFactory.IdentifierName(fieldName);
            editor.ReplaceNode(cameraAccess, replacement);

            return editor.GetChangedDocument();
        }
    }
}
