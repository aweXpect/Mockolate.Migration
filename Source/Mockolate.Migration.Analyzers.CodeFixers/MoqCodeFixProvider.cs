using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#pragma warning disable S1192 // String literals should not be duplicated
namespace Mockolate.Migration.Analyzers;

/// <summary>
///     A code fix provider that migrates Moq to Mockolate.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MoqCodeFixProvider))]
[Shared]
public class MoqCodeFixProvider() : AssertionCodeFixProvider(Rules.MoqRule)
{
	/// <inheritdoc />
	protected override async Task<Document> ConvertAssertionAsync(CodeFixContext context,
		ExpressionSyntax expressionSyntax, CancellationToken cancellationToken)
	{
		Document document = context.Document;

		SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

		if (root is not CompilationUnitSyntax compilationUnit)
		{
			return document;
		}

		TypeSyntax? typeArgument = expressionSyntax switch
		{
			ObjectCreationExpressionSyntax
			{
				Type: GenericNameSyntax { TypeArgumentList.Arguments: { Count: 1, } args, },
				ArgumentList.Arguments.Count: 0,
				Initializer: null,
			} => args[0],
			ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0, Initializer: null, }
				=> await GetTypeArgumentFromSemanticModel(document, expressionSyntax, cancellationToken)
					.ConfigureAwait(false),
			_ => null,
		};

		if (typeArgument is null)
		{
			return document;
		}

		InvocationExpressionSyntax createMockCall = SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					typeArgument.WithoutTrivia(),
					SyntaxFactory.IdentifierName("CreateMock")))
			.WithTriviaFrom(expressionSyntax);

		TypeSyntax? declarationType = GetDeclarationTypeSyntax(expressionSyntax);
		if (declarationType is not null && declarationType is not IdentifierNameSyntax { IsVar: true, })
		{
			compilationUnit = compilationUnit.ReplaceNodes(
				[expressionSyntax, declarationType,],
				(original, _) => original == expressionSyntax
					? createMockCall
					: typeArgument.WithTriviaFrom(declarationType));
		}
		else
		{
			compilationUnit = compilationUnit.ReplaceNode(expressionSyntax, createMockCall);
		}

		bool hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == "Mockolate");
		if (!hasUsing)
		{
			UsingDirectiveSyntax usingDirective = BuildUsingDirective(compilationUnit, "Mockolate");
			compilationUnit = compilationUnit.AddUsings(usingDirective);
		}

		return document.WithSyntaxRoot(compilationUnit);
	}

	private static TypeSyntax? GetDeclarationTypeSyntax(ExpressionSyntax expressionSyntax) =>
		expressionSyntax.Parent switch
		{
			EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax decl, }, } => decl.Type,
			EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax prop, } => prop.Type,
			_ => null,
		};

	private static UsingDirectiveSyntax BuildUsingDirective(CompilationUnitSyntax compilationUnit, string namespaceName)
	{
		NameSyntax name = SyntaxFactory.ParseName(namespaceName);

		UsingDirectiveSyntax? template = compilationUnit.Usings.LastOrDefault();
		if (template is not null)
		{
			// Build a fresh, unmodified directive and copy only the semicolon token trivia
			// (line-ending style) from the template, avoiding accidental inheritance of
			// global/static/alias modifiers that would produce invalid or overly-broad usings.
			SyntaxToken semicolon = SyntaxFactory.Token(SyntaxKind.SemicolonToken)
				.WithTriviaFrom(template.SemicolonToken);
			return SyntaxFactory.UsingDirective(name).WithSemicolonToken(semicolon);
		}

		return SyntaxFactory.UsingDirective(name);
	}

	private static async Task<TypeSyntax?> GetTypeArgumentFromSemanticModel(
		Document document,
		ExpressionSyntax expressionSyntax,
		CancellationToken cancellationToken)
	{
		SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
		if (semanticModel is null)
		{
			return null;
		}

		TypeInfo typeInfo = semanticModel.GetTypeInfo(expressionSyntax, cancellationToken);
		if (typeInfo.ConvertedType is INamedTypeSymbol { TypeArguments.Length: 1, } namedType)
		{
			return SyntaxFactory.ParseTypeName(namedType.TypeArguments[0].ToDisplayString()).WithoutTrivia();
		}

		return null;
	}
}

#pragma warning restore S1192
