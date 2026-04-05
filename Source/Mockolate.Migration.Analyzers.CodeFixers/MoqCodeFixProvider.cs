using System.Collections.Generic;
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

		SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

		TypeSyntax? typeArgument = GetTypeArgument(expressionSyntax, semanticModel, cancellationToken);
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
		bool replaceDeclarationType = declarationType is not null && declarationType is not IdentifierNameSyntax { IsVar: true, };

		ISymbol? mockSymbol = GetDeclaredMockSymbol(semanticModel, expressionSyntax, cancellationToken);
		List<MemberAccessExpressionSyntax> objectAccesses = FindObjectAccesses(compilationUnit, semanticModel, mockSymbol, cancellationToken);
		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> setupCallReplacements =
			FindAndBuildSetupCallReplacements(compilationUnit, semanticModel, mockSymbol, cancellationToken);

		List<SyntaxNode> nodesToReplace = [expressionSyntax,];
		if (replaceDeclarationType)
		{
			nodesToReplace.Add(declarationType!);
		}

		nodesToReplace.AddRange(objectAccesses);
		nodesToReplace.AddRange(setupCallReplacements.Keys);

		compilationUnit = compilationUnit.ReplaceNodes(
			nodesToReplace,
			(original, _) =>
			{
				if (original == expressionSyntax)
				{
					return createMockCall;
				}

				if (replaceDeclarationType && original == declarationType)
				{
					return typeArgument.WithTriviaFrom(declarationType!);
				}

				if (original is InvocationExpressionSyntax invocation &&
				    setupCallReplacements.TryGetValue(invocation, out InvocationExpressionSyntax? setupReplacement))
				{
					return setupReplacement;
				}

				return original is MemberAccessExpressionSyntax memberAccess
					? memberAccess.Expression.WithTriviaFrom(memberAccess)
					: original;
			});

		bool hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == "Mockolate");
		if (!hasUsing)
		{
			UsingDirectiveSyntax usingDirective = BuildUsingDirective(compilationUnit, "Mockolate");
			compilationUnit = compilationUnit.AddUsings(usingDirective);
		}

		return document.WithSyntaxRoot(compilationUnit);
	}

	private static TypeSyntax? GetTypeArgument(ExpressionSyntax expressionSyntax, SemanticModel? semanticModel, CancellationToken cancellationToken) =>
		expressionSyntax switch
		{
			ObjectCreationExpressionSyntax
				{
					Type: GenericNameSyntax { TypeArgumentList.Arguments: { Count: 1, } args, },
					ArgumentList.Arguments.Count: 0,
					Initializer: null,
				}
				=> args[0],
			ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0, Initializer: null, }
				=> GetTypeArgumentFromSemanticModel(semanticModel, expressionSyntax, cancellationToken),
			_ => null,
		};

	private static TypeSyntax? GetDeclarationTypeSyntax(ExpressionSyntax expressionSyntax) =>
		expressionSyntax.Parent switch
		{
			EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax decl, }, } => decl.Type,
			EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax prop, } => prop.Type,
			_ => null,
		};

	private static ISymbol? GetDeclaredMockSymbol(SemanticModel? semanticModel, ExpressionSyntax expressionSyntax, CancellationToken cancellationToken)
	{
		if (semanticModel is null)
		{
			return null;
		}

		return expressionSyntax.Parent switch
		{
			EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator, }
				=> semanticModel.GetDeclaredSymbol(declarator, cancellationToken),
			EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax prop, }
				=> semanticModel.GetDeclaredSymbol(prop, cancellationToken),
			_ => null,
		};
	}

	private static List<MemberAccessExpressionSyntax> FindObjectAccesses(
		CompilationUnitSyntax compilationUnit,
		SemanticModel? semanticModel,
		ISymbol? mockSymbol,
		CancellationToken cancellationToken)
	{
		if (semanticModel is null || mockSymbol is null)
		{
			return [];
		}

		List<MemberAccessExpressionSyntax> result = [];
		foreach (MemberAccessExpressionSyntax memberAccess in compilationUnit.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
		{
			if (memberAccess.Name.Identifier.Text != "Object")
			{
				continue;
			}

			SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken);
			if (SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, mockSymbol))
			{
				result.Add(memberAccess);
			}
		}

		return result;
	}

	private static Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> FindAndBuildSetupCallReplacements(
		CompilationUnitSyntax compilationUnit,
		SemanticModel? semanticModel,
		ISymbol? mockSymbol,
		CancellationToken cancellationToken)
	{
		if (semanticModel is null || mockSymbol is null)
		{
			return [];
		}

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> result = [];
		foreach (InvocationExpressionSyntax invocation in compilationUnit.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			{
				continue;
			}

			if (memberAccess.Name.Identifier.Text != "Setup")
			{
				continue;
			}

			SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken);
			if (!SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, mockSymbol))
			{
				continue;
			}

			if (invocation.ArgumentList.Arguments.Count != 1 ||
			    invocation.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax lambda)
			{
				continue;
			}

			if (lambda.Body is not InvocationExpressionSyntax lambdaBody ||
			    lambdaBody.Expression is not MemberAccessExpressionSyntax lambdaMemberAccess)
			{
				continue;
			}

			string methodName = lambdaMemberAccess.Name.Identifier.Text;
			ArgumentListSyntax transformedArgs = TransformMoqItReferences(lambdaBody.ArgumentList);

			MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				memberAccess.Expression,
				SyntaxFactory.IdentifierName("Mock"));
			MemberAccessExpressionSyntax setupAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				mockAccess,
				SyntaxFactory.IdentifierName("Setup"));
			MemberAccessExpressionSyntax methodAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				setupAccess,
				SyntaxFactory.IdentifierName(methodName));

			InvocationExpressionSyntax replacement = SyntaxFactory
				.InvocationExpression(methodAccess, transformedArgs)
				.WithTriviaFrom(invocation);

			result[invocation] = replacement;
		}

		return result;
	}

	private static ArgumentListSyntax TransformMoqItReferences(ArgumentListSyntax args) => args.ReplaceNodes(
		args.DescendantNodes().OfType<InvocationExpressionSyntax>()
			.Where(inv => IsMoqItCall(inv, out _, out _)),
		(original, rewritten) => TransformMoqItInvocation(original, (InvocationExpressionSyntax)rewritten));

	private static SyntaxNode TransformMoqItInvocation(InvocationExpressionSyntax original,
		InvocationExpressionSyntax rewritten)
	{
		if (!IsMoqItCall(original, out string methodName, out TypeArgumentListSyntax? typeArgs))
		{
			return rewritten;
		}

		IdentifierNameSyntax itIdentifier = SyntaxFactory.IdentifierName("It");

		switch (methodName)
		{
			case "Is" when rewritten.ArgumentList.Arguments.Count == 1
			               && rewritten.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax:
				return BuildItInvocation(itIdentifier, "Satisfies", typeArgs, rewritten.ArgumentList)
					.WithTriviaFrom(original);

			case "Is" when rewritten.ArgumentList.Arguments.Count == 2:
				{
					ArgumentListSyntax oneArgList = SyntaxFactory.ArgumentList(
						SyntaxFactory.SeparatedList([rewritten.ArgumentList.Arguments[0],]));
					InvocationExpressionSyntax isInvocation =
						BuildItInvocation(itIdentifier, "Is", typeArgs, oneArgList)
							.WithTriviaFrom(original);
					return SyntaxFactory.InvocationExpression(
						SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							isInvocation,
							SyntaxFactory.IdentifierName("Using")),
						SyntaxFactory.ArgumentList(
							SyntaxFactory.SeparatedList([rewritten.ArgumentList.Arguments[1],])));
				}

			case "IsInRange" when rewritten.ArgumentList.Arguments.Count == 3:
				{
					ExpressionSyntax rangeArg = rewritten.ArgumentList.Arguments[2].Expression;
					ArgumentListSyntax twoArgList = SyntaxFactory.ArgumentList(
						SyntaxFactory.SeparatedList(rewritten.ArgumentList.Arguments.Take(2)));
					InvocationExpressionSyntax rangeInvocation =
						BuildItInvocation(itIdentifier, "IsInRange", typeArgs, twoArgList)
							.WithTriviaFrom(original);
					if (rangeArg is MemberAccessExpressionSyntax { Name.Identifier.Text: "Exclusive", })
					{
						return SyntaxFactory.InvocationExpression(
							SyntaxFactory.MemberAccessExpression(
								SyntaxKind.SimpleMemberAccessExpression,
								rangeInvocation,
								SyntaxFactory.IdentifierName("Exclusive")),
							SyntaxFactory.ArgumentList());
					}

					return rangeInvocation;
				}

			case "IsIn":
				return BuildItInvocation(itIdentifier, "IsOneOf", typeArgs, rewritten.ArgumentList)
					.WithTriviaFrom(original);

			case "IsRegex":
				{
					InvocationExpressionSyntax matchesInvocation =
						BuildItInvocation(itIdentifier, "Matches", null, rewritten.ArgumentList)
							.WithTriviaFrom(original);
					return SyntaxFactory.InvocationExpression(
						SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							matchesInvocation,
							SyntaxFactory.IdentifierName("AsRegex")),
						SyntaxFactory.ArgumentList());
				}

			default:
				return BuildItInvocation(itIdentifier, methodName, typeArgs, rewritten.ArgumentList)
					.WithTriviaFrom(original);
		}
	}

	private static bool IsMoqItCall(InvocationExpressionSyntax invocation, out string methodName,
		out TypeArgumentListSyntax? typeArgs)
	{
		if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
		    memberAccess.Expression is MemberAccessExpressionSyntax innerAccess &&
		    innerAccess.Expression is IdentifierNameSyntax { Identifier.Text: "Moq", } &&
		    innerAccess.Name.Identifier.Text == "It")
		{
			methodName = memberAccess.Name.Identifier.Text;
			typeArgs = (memberAccess.Name as GenericNameSyntax)?.TypeArgumentList;
			return true;
		}

		methodName = "";
		typeArgs = null;
		return false;
	}

	private static InvocationExpressionSyntax BuildItInvocation(
		IdentifierNameSyntax itIdentifier,
		string methodName,
		TypeArgumentListSyntax? typeArgs,
		ArgumentListSyntax args)
	{
		SimpleNameSyntax methodNameSyntax = typeArgs is not null
			? SyntaxFactory.GenericName(SyntaxFactory.Identifier(methodName), typeArgs)
			: SyntaxFactory.IdentifierName(methodName);

		return SyntaxFactory.InvocationExpression(
			SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				itIdentifier,
				methodNameSyntax),
			args);
	}

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

	private static TypeSyntax? GetTypeArgumentFromSemanticModel(
		SemanticModel? semanticModel,
		ExpressionSyntax expressionSyntax,
		CancellationToken cancellationToken)
	{
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
