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
#pragma warning disable S3776 // Cognitive Complexity of methods should not be too high
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

		InvocationExpressionSyntax createMockInvocation = SyntaxFactory.InvocationExpression(
			SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				typeArgument.WithoutTrivia(),
				SyntaxFactory.IdentifierName("CreateMock")));

		if (HasStrictMockBehavior(expressionSyntax, semanticModel, cancellationToken))
		{
			ExpressionSyntax throwingWhenNotSetup = SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							SyntaxFactory.IdentifierName("Mockolate"),
							SyntaxFactory.IdentifierName("MockBehavior")),
						SyntaxFactory.IdentifierName("Default")),
					SyntaxFactory.IdentifierName("ThrowingWhenNotSetup")));

			createMockInvocation = createMockInvocation.WithArgumentList(
				SyntaxFactory.ArgumentList(
					SyntaxFactory.SingletonSeparatedList(
						SyntaxFactory.Argument(throwingWhenNotSetup))));
		}

		InvocationExpressionSyntax createMockCall = createMockInvocation.WithTriviaFrom(expressionSyntax);

		TypeSyntax? declarationType = GetDeclarationTypeSyntax(expressionSyntax);
		bool replaceDeclarationType = declarationType is not null && declarationType is not IdentifierNameSyntax { IsVar: true, };

		ISymbol? mockSymbol = GetDeclaredMockSymbol(semanticModel, expressionSyntax, cancellationToken);
		IReadOnlyList<InvocationExpressionSyntax> allInvocations =
			compilationUnit.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

		List<MemberAccessExpressionSyntax> objectAccesses = FindObjectAccesses(compilationUnit, semanticModel, mockSymbol, cancellationToken);
		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> setupCallReplacements =
			FindAndBuildSetupCallReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		Dictionary<InvocationExpressionSyntax, MemberAccessExpressionSyntax> setupPropertyAccessReplacements =
			FindAndBuildSetupPropertyAccessReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> setupPropertyCallReplacements =
			FindAndBuildSetupPropertyCallReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> callbackReplacements =
			FindAndBuildCallbackReplacements(allInvocations, setupCallReplacements, out HashSet<InvocationExpressionSyntax> setupsWrappedByCallbacks);

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> verifyCallReplacements =
			FindAndBuildVerifyCallReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> verifyEventCallReplacements =
			FindAndBuildVerifyEventCallReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> raiseCallReplacements =
			FindAndBuildRaiseCallReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		List<SyntaxNode> nodesToReplace = [expressionSyntax,];
		if (replaceDeclarationType)
		{
			nodesToReplace.Add(declarationType!);
		}

		nodesToReplace.AddRange(objectAccesses);
		nodesToReplace.AddRange(setupCallReplacements.Keys.Where(k => !setupsWrappedByCallbacks.Contains(k)));
		nodesToReplace.AddRange(setupPropertyAccessReplacements.Keys);
		nodesToReplace.AddRange(setupPropertyCallReplacements.Keys);
		nodesToReplace.AddRange(callbackReplacements.Keys);
		nodesToReplace.AddRange(verifyCallReplacements.Keys);
		nodesToReplace.AddRange(verifyEventCallReplacements.Keys);
		nodesToReplace.AddRange(raiseCallReplacements.Keys);

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

				if (original is InvocationExpressionSyntax invocation)
				{
					if (callbackReplacements.TryGetValue(invocation, out InvocationExpressionSyntax? callbackReplacement))
					{
						return callbackReplacement;
					}

					if (setupCallReplacements.TryGetValue(invocation, out InvocationExpressionSyntax? setupReplacement))
					{
						return setupReplacement;
					}

					if (setupPropertyAccessReplacements.TryGetValue(invocation, out MemberAccessExpressionSyntax? propertyAccessReplacement))
					{
						return propertyAccessReplacement;
					}

					if (setupPropertyCallReplacements.TryGetValue(invocation, out InvocationExpressionSyntax? setupPropertyCallReplacement))
					{
						return setupPropertyCallReplacement;
					}

					if (verifyCallReplacements.TryGetValue(invocation, out InvocationExpressionSyntax? verifyReplacement))
					{
						return verifyReplacement;
					}

					if (verifyEventCallReplacements.TryGetValue(invocation, out InvocationExpressionSyntax? verifyEventReplacement))
					{
						return verifyEventReplacement;
					}

					if (raiseCallReplacements.TryGetValue(invocation, out InvocationExpressionSyntax? raiseReplacement))
					{
						return raiseReplacement;
					}
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

		if (verifyCallReplacements.Count > 0 || verifyEventCallReplacements.Count > 0)
		{
			bool hasVerifyUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == "Mockolate.Verify");
			if (!hasVerifyUsing)
			{
				UsingDirectiveSyntax verifyUsingDirective = BuildUsingDirective(compilationUnit, "Mockolate.Verify");
				compilationUnit = compilationUnit.AddUsings(verifyUsingDirective);
			}
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
			ObjectCreationExpressionSyntax
				{
					Type: GenericNameSyntax { TypeArgumentList.Arguments: { Count: 1, } args, },
					ArgumentList: { Arguments: { Count: 1, } arguments, },
					Initializer: null,
				} when IsMockBehaviorArgument(arguments[0], semanticModel, cancellationToken)
				=> args[0],
			ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0, Initializer: null, }
				=> GetTypeArgumentFromSemanticModel(semanticModel, expressionSyntax, cancellationToken),
			_ => null,
		};

	private static bool IsMockBehaviorArgument(ArgumentSyntax argument, SemanticModel? semanticModel, CancellationToken cancellationToken)
	{
		if (semanticModel is not null)
		{
			ITypeSymbol? type = semanticModel.GetTypeInfo(argument.Expression, cancellationToken).Type;
			return type?.ToDisplayString() == "Moq.MockBehavior";
		}

		string text = argument.Expression.ToString();
		return text is "MockBehavior.Strict" or "Moq.MockBehavior.Strict"
			or "MockBehavior.Loose" or "Moq.MockBehavior.Loose"
			or "MockBehavior.Default" or "Moq.MockBehavior.Default";
	}

	private static bool HasStrictMockBehavior(ExpressionSyntax expressionSyntax, SemanticModel? semanticModel, CancellationToken cancellationToken)
	{
		if (expressionSyntax is not ObjectCreationExpressionSyntax { ArgumentList.Arguments: { Count: 1, } arguments, })
		{
			return false;
		}

		if (semanticModel is not null)
		{
			ISymbol? symbol = semanticModel.GetSymbolInfo(arguments[0].Expression, cancellationToken).Symbol;
			return symbol?.ToDisplayString() == "Moq.MockBehavior.Strict";
		}

		string text = arguments[0].Expression.ToString();
		return text is "MockBehavior.Strict" or "Moq.MockBehavior.Strict";
	}

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

	private static Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> FindAndBuildRaiseCallReplacements(
		IReadOnlyList<InvocationExpressionSyntax> allInvocations,
		SemanticModel? semanticModel,
		ISymbol? mockSymbol,
		CancellationToken cancellationToken)
	{
		if (semanticModel is null || mockSymbol is null)
		{
			return [];
		}

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> result = [];
		foreach (InvocationExpressionSyntax invocation in allInvocations)
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			{
				continue;
			}

			if (memberAccess.Name.Identifier.Text != "Raise")
			{
				continue;
			}

			SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken);
			if (!SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, mockSymbol))
			{
				continue;
			}

			if (invocation.ArgumentList.Arguments.Count < 1 ||
			    invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
			{
				continue;
			}

			if (lambda.Body is not AssignmentExpressionSyntax assignment ||
			    !assignment.IsKind(SyntaxKind.AddAssignmentExpression) ||
			    assignment.Left is not MemberAccessExpressionSyntax eventAccess)
			{
				continue;
			}

			string? lambdaParamName = GetSingleLambdaParamName(lambda);

			if (lambdaParamName is null)
			{
				continue;
			}

			if (eventAccess.Expression is not IdentifierNameSyntax { Identifier.Text: var paramName, } ||
			    paramName != lambdaParamName)
			{
				continue;
			}

			SimpleNameSyntax eventNameSyntax = eventAccess.Name;

			ArgumentListSyntax eventArgs;
			if (invocation.ArgumentList.Arguments.Count == 2 &&
			    IsEventArgsType(semanticModel, invocation.ArgumentList.Arguments[1].Expression, cancellationToken))
			{
				// Single EventArgs argument: prepend null as sender
				eventArgs = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
				{
					SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)), invocation.ArgumentList.Arguments[1],
				}));
			}
			else
			{
				eventArgs = SyntaxFactory.ArgumentList(
					SyntaxFactory.SeparatedList(invocation.ArgumentList.Arguments.Skip(1)));
			}

			MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				memberAccess.Expression,
				SyntaxFactory.IdentifierName("Mock"));
			MemberAccessExpressionSyntax raiseAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				mockAccess,
				SyntaxFactory.IdentifierName("Raise"));
			MemberAccessExpressionSyntax eventMethodAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				raiseAccess,
				eventNameSyntax);
			InvocationExpressionSyntax replacement = SyntaxFactory.InvocationExpression(eventMethodAccess, eventArgs)
				.WithTriviaFrom(invocation);

			result[invocation] = replacement;
		}

		return result;
	}

	private static bool IsEventArgsType(SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken cancellationToken)
	{
		TypeInfo typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
		ITypeSymbol? type = typeInfo.Type ?? typeInfo.ConvertedType;
		ITypeSymbol? current = type;
		while (current is not null)
		{
			if (current.ContainingNamespace?.ToString() == "System" && current.Name == "EventArgs")
			{
				return true;
			}

			current = (current as INamedTypeSymbol)?.BaseType;
		}

		return false;
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
		IReadOnlyList<InvocationExpressionSyntax> allInvocations,
		SemanticModel? semanticModel,
		ISymbol? mockSymbol,
		CancellationToken cancellationToken)
	{
		if (semanticModel is null || mockSymbol is null)
		{
			return [];
		}

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> result = [];
		foreach (InvocationExpressionSyntax invocation in allInvocations)
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
			    invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
			{
				continue;
			}

			if (lambda.Body is not InvocationExpressionSyntax lambdaBody ||
			    lambdaBody.Expression is not MemberAccessExpressionSyntax lambdaMemberAccess)
			{
				continue;
			}

			string? lambdaParamName = GetSingleLambdaParamName(lambda);

			if (lambdaParamName is null)
			{
				continue;
			}

			// Walk the receiver chain to collect navigation properties between the lambda
			// parameter and the final method call, e.g. m => m.Child.GrandChild.Bar(...)
			// yields ["Child", "GrandChild"]. Returns null when not rooted at the lambda param.
			List<SimpleNameSyntax>? navigationChain = ExtractNavigationChain(lambdaMemberAccess.Expression, lambdaParamName);
			if (navigationChain is null)
			{
				continue;
			}

			SimpleNameSyntax methodNameSyntax = lambdaMemberAccess.Name;
			ArgumentListSyntax transformedArgs = TransformMoqItReferences(
				TransformRefAndOutArguments(lambdaBody.ArgumentList, semanticModel, cancellationToken));

			InvocationExpressionSyntax replacement;
			if (navigationChain.Count == 0)
			{
				// Direct setup: mock.Mock.Setup.Method(args)
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
					methodNameSyntax);
				replacement = SyntaxFactory.InvocationExpression(methodAccess, transformedArgs)
					.WithTriviaFrom(invocation);
			}
			else
			{
				// Nested setup: mock.Nav1.Nav2.Mock.Method(args)
				ExpressionSyntax navChain = memberAccess.Expression;
				foreach (SimpleNameSyntax nav in navigationChain)
				{
					navChain = SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						navChain,
						nav);
				}

				MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					navChain,
					SyntaxFactory.IdentifierName("Mock"));
				MemberAccessExpressionSyntax methodAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					mockAccess,
					methodNameSyntax);
				replacement = SyntaxFactory.InvocationExpression(methodAccess, transformedArgs)
					.WithTriviaFrom(invocation);
			}

			result[invocation] = replacement;
		}

		return result;
	}

	private static Dictionary<InvocationExpressionSyntax, MemberAccessExpressionSyntax> FindAndBuildSetupPropertyAccessReplacements(
		IReadOnlyList<InvocationExpressionSyntax> allInvocations,
		SemanticModel? semanticModel,
		ISymbol? mockSymbol,
		CancellationToken cancellationToken)
	{
		if (semanticModel is null || mockSymbol is null)
		{
			return [];
		}

		Dictionary<InvocationExpressionSyntax, MemberAccessExpressionSyntax> result = [];
		foreach (InvocationExpressionSyntax invocation in allInvocations)
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
			    invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
			{
				continue;
			}

			// Only handle property access (not method calls — those are handled by FindAndBuildSetupCallReplacements)
			if (lambda.Body is not MemberAccessExpressionSyntax lambdaMemberAccess)
			{
				continue;
			}

			string? lambdaParamName = GetSingleLambdaParamName(lambda);

			if (lambdaParamName is null)
			{
				continue;
			}

			List<SimpleNameSyntax>? navigationChain = ExtractNavigationChain(lambdaMemberAccess.Expression, lambdaParamName);
			if (navigationChain is null)
			{
				continue;
			}

			SimpleNameSyntax propertyNameSyntax = lambdaMemberAccess.Name;

			MemberAccessExpressionSyntax replacement;
			if (navigationChain.Count == 0)
			{
				// Direct setup: mock.Mock.Setup.Property
				MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					memberAccess.Expression,
					SyntaxFactory.IdentifierName("Mock"));
				MemberAccessExpressionSyntax setupAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					mockAccess,
					SyntaxFactory.IdentifierName("Setup"));
				replacement = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					setupAccess,
					propertyNameSyntax);
			}
			else
			{
				// Nested setup: mock.Nav1.Nav2.Mock.Setup.Property
				ExpressionSyntax navChain = memberAccess.Expression;
				foreach (SimpleNameSyntax nav in navigationChain)
				{
					navChain = SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						navChain,
						nav);
				}

				MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					navChain,
					SyntaxFactory.IdentifierName("Mock"));
				MemberAccessExpressionSyntax setupAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					mockAccess,
					SyntaxFactory.IdentifierName("Setup"));
				replacement = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					setupAccess,
					propertyNameSyntax);
			}

			result[invocation] = replacement.WithTriviaFrom(invocation);
		}

		return result;
	}

	private static Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> FindAndBuildSetupPropertyCallReplacements(
		IReadOnlyList<InvocationExpressionSyntax> allInvocations,
		SemanticModel? semanticModel,
		ISymbol? mockSymbol,
		CancellationToken cancellationToken)
	{
		if (semanticModel is null || mockSymbol is null)
		{
			return [];
		}

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> result = [];
		foreach (InvocationExpressionSyntax invocation in allInvocations)
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			{
				continue;
			}

			if (memberAccess.Name.Identifier.Text != "SetupProperty")
			{
				continue;
			}

			SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken);
			if (!SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, mockSymbol))
			{
				continue;
			}

			if (invocation.ArgumentList.Arguments.Count is 0 or > 2 ||
			    invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
			{
				continue;
			}

			if (lambda.Body is not MemberAccessExpressionSyntax lambdaMemberAccess)
			{
				continue;
			}

			string? lambdaParamName = GetSingleLambdaParamName(lambda);

			if (lambdaParamName is null)
			{
				continue;
			}

			List<SimpleNameSyntax>? navigationChain = ExtractNavigationChain(lambdaMemberAccess.Expression, lambdaParamName);
			if (navigationChain is null)
			{
				continue;
			}

			SimpleNameSyntax propertyNameSyntax = lambdaMemberAccess.Name;

			ExpressionSyntax propertyAccess;
			if (navigationChain.Count == 0)
			{
				MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					memberAccess.Expression,
					SyntaxFactory.IdentifierName("Mock"));
				MemberAccessExpressionSyntax setupAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					mockAccess,
					SyntaxFactory.IdentifierName("Setup"));
				propertyAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					setupAccess,
					propertyNameSyntax);
			}
			else
			{
				ExpressionSyntax navChain = memberAccess.Expression;
				foreach (SimpleNameSyntax nav in navigationChain)
				{
					navChain = SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						navChain,
						nav);
				}

				MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					navChain,
					SyntaxFactory.IdentifierName("Mock"));
				MemberAccessExpressionSyntax setupAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					mockAccess,
					SyntaxFactory.IdentifierName("Setup"));
				propertyAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					setupAccess,
					propertyNameSyntax);
			}

			InvocationExpressionSyntax replacement;
			if (invocation.ArgumentList.Arguments.Count == 2)
			{
				// mock.SetupProperty(f => f.Name, "foo") → mock.Mock.Setup.Name.InitializeWith("foo")
				ArgumentSyntax defaultValueArg = invocation.ArgumentList.Arguments[1];
				replacement = SyntaxFactory.InvocationExpression(
						SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							propertyAccess,
							SyntaxFactory.IdentifierName("InitializeWith")),
						SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList([defaultValueArg,])))
					.WithTriviaFrom(invocation);
			}
			else
			{
				// mock.SetupProperty(f => f.Name) → mock.Mock.Setup.Name.Register()
				replacement = SyntaxFactory.InvocationExpression(
						SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							propertyAccess,
							SyntaxFactory.IdentifierName("Register")),
						SyntaxFactory.ArgumentList())
					.WithTriviaFrom(invocation);
			}

			result[invocation] = replacement;
		}

		return result;
	}

	private static Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> FindAndBuildCallbackReplacements(
		IReadOnlyList<InvocationExpressionSyntax> allInvocations,
		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> setupCallReplacements,
		out HashSet<InvocationExpressionSyntax> setupsWrappedByCallbacks)
	{
		setupsWrappedByCallbacks = [];
		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> result = [];
		foreach (InvocationExpressionSyntax invocation in allInvocations)
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			{
				continue;
			}

			if (memberAccess.Name.Identifier.Text != "Callback")
			{
				continue;
			}

			// Walk the receiver chain to find a Setup invocation we're migrating.
			// Callback may appear anywhere in the chain (e.g. Setup(...).Returns(...).Callback(...)).
			if (!TryRebuildReceiverWithMigratedSetup(memberAccess.Expression, setupCallReplacements,
				    out ExpressionSyntax? rebuiltReceiver, out InvocationExpressionSyntax? wrappedSetup))
			{
				continue;
			}

			// Track that this setup must not be independently replaced (it is embedded in the
			// Callback replacement below, already in its migrated form).
			setupsWrappedByCallbacks.Add(wrappedSetup!);

			// Replace .Callback<T1, T2, ...>(action) with .Do(action) — type args are dropped
			// because Mockolate infers them during code generation.
			// Reuse the original dot token to preserve leading trivia (e.g. newline + indent
			// when .Callback is written on its own line).
			InvocationExpressionSyntax replacement = SyntaxFactory.InvocationExpression(
					SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						rebuiltReceiver!,
						memberAccess.OperatorToken,
						SyntaxFactory.IdentifierName("Do")),
					invocation.ArgumentList)
				.WithTriviaFrom(invocation);

			result[invocation] = replacement;
		}

		return result;
	}

	/// <summary>
	///     Walks <paramref name="expression" /> looking for a Setup invocation that is present in
	///     <paramref name="setupCallReplacements" />.  When found, returns a rebuilt expression with
	///     the Setup node substituted by its migrated replacement, preserving all intermediate
	///     chained calls (e.g. .Returns / .Throws) between the Setup and the Callback.
	/// </summary>
	private static bool TryRebuildReceiverWithMigratedSetup(
		ExpressionSyntax expression,
		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> setupCallReplacements,
		out ExpressionSyntax? rebuilt,
		out InvocationExpressionSyntax? foundSetup)
	{
		// Base case: this expression itself is a Setup call we're migrating.
		if (expression is InvocationExpressionSyntax inv &&
		    setupCallReplacements.TryGetValue(inv, out InvocationExpressionSyntax? migratedSetup))
		{
			rebuilt = migratedSetup;
			foundSetup = inv;
			return true;
		}

		// Recursive case: an invocation chained on something (e.g. .Returns(true) on top of Setup).
		if (expression is InvocationExpressionSyntax chainedInv &&
		    chainedInv.Expression is MemberAccessExpressionSyntax chainedMemberAccess &&
		    TryRebuildReceiverWithMigratedSetup(chainedMemberAccess.Expression, setupCallReplacements,
			    out ExpressionSyntax? rebuiltInner, out foundSetup))
		{
			MemberAccessExpressionSyntax newMemberAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				rebuiltInner!,
				chainedMemberAccess.OperatorToken,
				chainedMemberAccess.Name);
			rebuilt = SyntaxFactory.InvocationExpression(newMemberAccess, chainedInv.ArgumentList);
			return true;
		}

		rebuilt = null;
		foundSetup = null;
		return false;
	}

	private static Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> FindAndBuildVerifyCallReplacements(
		IReadOnlyList<InvocationExpressionSyntax> allInvocations,
		SemanticModel? semanticModel,
		ISymbol? mockSymbol,
		CancellationToken cancellationToken)
	{
		if (semanticModel is null || mockSymbol is null)
		{
			return [];
		}

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> result = [];
		foreach (InvocationExpressionSyntax invocation in allInvocations)
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			{
				continue;
			}

			if (memberAccess.Name.Identifier.Text != "Verify")
			{
				continue;
			}

			SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken);
			if (!SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, mockSymbol))
			{
				continue;
			}

			if (invocation.ArgumentList.Arguments.Count is 0 or > 2 ||
			    invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
			{
				continue;
			}

			if (lambda.Body is not InvocationExpressionSyntax lambdaBody ||
			    lambdaBody.Expression is not MemberAccessExpressionSyntax lambdaMemberAccess)
			{
				continue;
			}

			string? lambdaParamName = GetSingleLambdaParamName(lambda);

			if (lambdaParamName is null)
			{
				continue;
			}

			List<SimpleNameSyntax>? navigationChain = ExtractNavigationChain(lambdaMemberAccess.Expression, lambdaParamName);
			if (navigationChain is null)
			{
				continue;
			}

			SimpleNameSyntax methodNameSyntax = lambdaMemberAccess.Name;
			ArgumentListSyntax transformedArgs = TransformMoqItReferences(
				TransformRefAndOutArguments(lambdaBody.ArgumentList, semanticModel, cancellationToken));

			InvocationExpressionSyntax baseInvocation;
			if (navigationChain.Count == 0)
			{
				MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					memberAccess.Expression,
					SyntaxFactory.IdentifierName("Mock"));
				MemberAccessExpressionSyntax verifyAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					mockAccess,
					SyntaxFactory.IdentifierName("Verify"));
				MemberAccessExpressionSyntax methodAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					verifyAccess,
					methodNameSyntax);
				baseInvocation = SyntaxFactory.InvocationExpression(methodAccess, transformedArgs);
			}
			else
			{
				ExpressionSyntax navChain = memberAccess.Expression;
				foreach (SimpleNameSyntax nav in navigationChain)
				{
					navChain = SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						navChain,
						nav);
				}

				MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					navChain,
					SyntaxFactory.IdentifierName("Mock"));
				MemberAccessExpressionSyntax verifyAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					mockAccess,
					SyntaxFactory.IdentifierName("Verify"));
				MemberAccessExpressionSyntax methodAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					verifyAccess,
					methodNameSyntax);
				baseInvocation = SyntaxFactory.InvocationExpression(methodAccess, transformedArgs);
			}

			InvocationExpressionSyntax replacement;
			if (invocation.ArgumentList.Arguments.Count == 2)
			{
				ExpressionSyntax timesArg = invocation.ArgumentList.Arguments[1].Expression;
				InvocationExpressionSyntax? withTimes = ApplyTimesChain(baseInvocation, timesArg);
				if (withTimes is null)
				{
					continue;
				}

				replacement = withTimes.WithTriviaFrom(invocation);
			}
			else
			{
				replacement = SyntaxFactory.InvocationExpression(
						SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							baseInvocation,
							SyntaxFactory.IdentifierName("AtLeastOnce")),
						SyntaxFactory.ArgumentList())
					.WithTriviaFrom(invocation);
			}

			result[invocation] = replacement;
		}

		return result;
	}

	private static Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> FindAndBuildVerifyEventCallReplacements(
		IReadOnlyList<InvocationExpressionSyntax> allInvocations,
		SemanticModel? semanticModel,
		ISymbol? mockSymbol,
		CancellationToken cancellationToken)
	{
		if (semanticModel is null || mockSymbol is null)
		{
			return [];
		}

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> result = [];
		foreach (InvocationExpressionSyntax invocation in allInvocations)
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			{
				continue;
			}

			string methodName = memberAccess.Name.Identifier.Text;
			string? eventSubscriptionMethod = methodName switch
			{
				"VerifyAdd" => "Subscribed",
				"VerifyRemove" => "Unsubscribed",
				_ => null,
			};

			if (eventSubscriptionMethod is null)
			{
				continue;
			}

			SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken);
			if (!SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, mockSymbol))
			{
				continue;
			}

			if (invocation.ArgumentList.Arguments.Count is 0 or > 2 ||
			    invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
			{
				continue;
			}

			SyntaxKind expectedAssignment = methodName == "VerifyAdd"
				? SyntaxKind.AddAssignmentExpression
				: SyntaxKind.SubtractAssignmentExpression;

			if (lambda.Body is not AssignmentExpressionSyntax assignment ||
			    !assignment.IsKind(expectedAssignment) ||
			    assignment.Left is not MemberAccessExpressionSyntax eventAccess)
			{
				continue;
			}

			// LHS of += / -= must bind to an event, not a delegate field/property — otherwise
			// the generated Verify.<name>.Subscribed() chain would reference a non-existent member.
			if (semanticModel.GetSymbolInfo(eventAccess, cancellationToken).Symbol is not IEventSymbol)
			{
				continue;
			}

			string? lambdaParamName = GetSingleLambdaParamName(lambda);
			if (lambdaParamName is null)
			{
				continue;
			}

			List<SimpleNameSyntax>? navigationChain = ExtractNavigationChain(eventAccess.Expression, lambdaParamName);
			if (navigationChain is null)
			{
				continue;
			}

			SimpleNameSyntax eventNameSyntax = eventAccess.Name;

			ExpressionSyntax receiver = memberAccess.Expression;
			foreach (SimpleNameSyntax nav in navigationChain)
			{
				receiver = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					receiver,
					nav);
			}

			MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				receiver,
				SyntaxFactory.IdentifierName("Mock"));
			MemberAccessExpressionSyntax verifyAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				mockAccess,
				SyntaxFactory.IdentifierName("Verify"));
			MemberAccessExpressionSyntax eventMemberAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				verifyAccess,
				eventNameSyntax);
			InvocationExpressionSyntax baseInvocation = SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					eventMemberAccess,
					SyntaxFactory.IdentifierName(eventSubscriptionMethod)),
				SyntaxFactory.ArgumentList());

			InvocationExpressionSyntax atLeastOnceFallback = SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					baseInvocation,
					SyntaxFactory.IdentifierName("AtLeastOnce")),
				SyntaxFactory.ArgumentList());

			// Fall back to AtLeastOnce when the Times argument can't be translated — the
			// Mock<T>() construction is unconditionally rewritten, so leaving the original
			// VerifyAdd/VerifyRemove in place would produce non-compiling code.
			InvocationExpressionSyntax replacement = invocation.ArgumentList.Arguments.Count == 2
				? (ApplyTimesChain(baseInvocation, invocation.ArgumentList.Arguments[1].Expression) ?? atLeastOnceFallback)
				.WithTriviaFrom(invocation)
				: atLeastOnceFallback.WithTriviaFrom(invocation);

			result[invocation] = replacement;
		}

		return result;
	}

	private static InvocationExpressionSyntax? ApplyTimesChain(
		InvocationExpressionSyntax baseInvocation, ExpressionSyntax timesArg)
	{
		(string? methodName, ArgumentListSyntax? methodArgs) = ExtractTimesMethodCall(timesArg);
		if (methodName is null || methodArgs is null)
		{
			return null;
		}

		return SyntaxFactory.InvocationExpression(
			SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				baseInvocation,
				SyntaxFactory.IdentifierName(methodName)),
			methodArgs);
	}

	private static (string? MethodName, ArgumentListSyntax? Args) ExtractTimesMethodCall(ExpressionSyntax timesArg)
	{
		switch (timesArg)
		{
			// Times.Never / Times.Once / Times.AtLeastOnce / Times.AtMostOnce  (property access, no call)
			case MemberAccessExpressionSyntax { Name.Identifier.Text: var name, Expression: var expr, }
				when IsTimesExpression(expr) && IsNoArgTimesName(name):
				return (name, SyntaxFactory.ArgumentList());

			// Times.Never() / Times.Once() / Times.AtLeastOnce() / Times.AtMostOnce()  (0-arg call)
			case InvocationExpressionSyntax
				{
					Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: var name, Expression: var expr, },
					ArgumentList.Arguments.Count: 0,
				}
				when IsTimesExpression(expr) && IsNoArgTimesName(name):
				return (name, SyntaxFactory.ArgumentList());

			// Times.AtLeast(n) / Times.AtMost(n) / Times.Exactly(n)
			case InvocationExpressionSyntax
				{
					Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: var name, Expression: var expr, },
					ArgumentList: { Arguments.Count: 1, } argList,
				}
				when IsTimesExpression(expr) && IsOneArgTimesName(name):
				return (name, argList);

			// Times.Between(min, max, Range.Inclusive/Exclusive)
			case InvocationExpressionSyntax
				{
					Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Between", Expression: var expr, },
					ArgumentList: { Arguments.Count: 3, } argList,
				}
				when IsTimesExpression(expr):
				{
					bool isExclusive = argList.Arguments[2].Expression is
						MemberAccessExpressionSyntax { Name.Identifier.Text: "Exclusive", };

					if (isExclusive)
					{
						ExpressionSyntax minExpr = AdjustIntBoundary(argList.Arguments[0].Expression, 1);
						ExpressionSyntax maxExpr = AdjustIntBoundary(argList.Arguments[1].Expression, -1);
						return ("Between", SyntaxFactory.ArgumentList(
							SyntaxFactory.SeparatedList([
								SyntaxFactory.Argument(minExpr),
								SyntaxFactory.Argument(maxExpr),
							])));
					}

					return ("Between", SyntaxFactory.ArgumentList(
						SyntaxFactory.SeparatedList(argList.Arguments.Take(2))));
				}

			default:
				return (null, null);
		}
	}

	private static bool IsTimesExpression(ExpressionSyntax expression) =>
		expression is IdentifierNameSyntax { Identifier.Text: "Times", }
		|| expression is MemberAccessExpressionSyntax
		{
			Expression: IdentifierNameSyntax { Identifier.Text: "Moq", },
			Name.Identifier.Text: "Times",
		};

	private static bool IsNoArgTimesName(string name) =>
		name is "Never" or "Once" or "AtLeastOnce" or "AtMostOnce";

	private static bool IsOneArgTimesName(string name) =>
		name is "AtLeast" or "AtMost" or "Exactly";

	private static ExpressionSyntax AdjustIntBoundary(ExpressionSyntax expr, int delta)
	{
		if (expr is LiteralExpressionSyntax literal && literal.Token.Value is int value)
		{
			return SyntaxFactory.LiteralExpression(
				SyntaxKind.NumericLiteralExpression,
				SyntaxFactory.Literal(value + delta));
		}

		if (delta > 0)
		{
			return SyntaxFactory.BinaryExpression(
				SyntaxKind.AddExpression,
				expr,
				SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(delta)));
		}

		return SyntaxFactory.BinaryExpression(
			SyntaxKind.SubtractExpression,
			expr,
			SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(-delta)));
	}

	private static string? GetSingleLambdaParamName(LambdaExpressionSyntax lambda) =>
		lambda switch
		{
			SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
			ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: { Count: 1, } parms, }
				=> parms[0].Identifier.Text,
			_ => null,
		};

	private static List<SimpleNameSyntax>? ExtractNavigationChain(ExpressionSyntax expression, string lambdaParamName)
	{
		List<SimpleNameSyntax> chain = [];
		ExpressionSyntax current = expression;
		while (true)
		{
			if (current is IdentifierNameSyntax id && id.Identifier.Text == lambdaParamName)
			{
				chain.Reverse();
				return chain;
			}

			if (current is MemberAccessExpressionSyntax memberAccess)
			{
				chain.Add(memberAccess.Name);
				current = memberAccess.Expression;
			}
			else
			{
				return null;
			}
		}
	}

	private static ArgumentListSyntax TransformMoqItReferences(ArgumentListSyntax args) => args.ReplaceNodes(
		args.DescendantNodes().OfType<InvocationExpressionSyntax>()
			.Where(inv => IsMoqItCall(inv, out _, out _)),
		TransformMoqItInvocation);

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
		if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
		{
			methodName = "";
			typeArgs = null;
			return false;
		}

		bool isIt = memberAccess.Expression switch
		{
			// It.Method(...) - unqualified, with using Moq
			IdentifierNameSyntax { Identifier.Text: "It", } => true,
			// Moq.It.Method(...) - explicitly qualified
			MemberAccessExpressionSyntax
			{
				Expression: IdentifierNameSyntax { Identifier.Text: "Moq", },
				Name.Identifier.Text: "It",
			} => true,
			// global::Moq.It.Method(...) - alias-qualified
			MemberAccessExpressionSyntax
			{
				Expression: AliasQualifiedNameSyntax { Name.Identifier.Text: "Moq", },
				Name.Identifier.Text: "It",
			} => true,
			_ => false,
		};

		if (!isIt)
		{
			methodName = "";
			typeArgs = null;
			return false;
		}

		methodName = memberAccess.Name.Identifier.Text;
		typeArgs = (memberAccess.Name as GenericNameSyntax)?.TypeArgumentList;
		return true;
	}

	private static ArgumentListSyntax TransformRefAndOutArguments(
		ArgumentListSyntax args,
		SemanticModel? semanticModel,
		CancellationToken cancellationToken)
	{
		IdentifierNameSyntax itIdentifier = SyntaxFactory.IdentifierName("It");

		return args.ReplaceNodes(
			args.Arguments.Where(a =>
				a.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) ||
				a.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)),
			(original, _) =>
			{
				ExpressionSyntax expr = original.Expression.WithoutTrivia();

				if (original.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
				{
					// out varName → It.IsOut(() => varName)
					ParenthesizedLambdaExpressionSyntax lambda = SyntaxFactory.ParenthesizedLambdaExpression(expr);
					InvocationExpressionSyntax isOut = BuildItInvocation(
						itIdentifier, "IsOut", null,
						SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList([SyntaxFactory.Argument(lambda),])));
					return SyntaxFactory.Argument(isOut).WithTriviaFrom(original);
				}

				// ref Moq.It.Ref<T>.IsAny → It.IsAnyRef<T>()
				if (TryExtractItRefIsAnyTypeArgs(original.Expression, out TypeArgumentListSyntax? isAnyTypeArgs))
				{
					InvocationExpressionSyntax isAnyRef = BuildItInvocation(
						itIdentifier, "IsAnyRef", isAnyTypeArgs,
						SyntaxFactory.ArgumentList());
					return SyntaxFactory.Argument(isAnyRef).WithTriviaFrom(original);
				}

				// ref expr → It.IsRef<T>(_ => expr)
				TypeArgumentListSyntax? typeArgs = null;
				if (semanticModel is not null)
				{
					ITypeSymbol? typeSymbol = semanticModel.GetTypeInfo(original.Expression, cancellationToken).Type;
					if (typeSymbol is not null)
					{
						TypeSyntax typeSyntax = SyntaxFactory.ParseTypeName(
							typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)).WithoutTrivia();
						typeArgs = SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList([typeSyntax,]));
					}
				}

				SimpleLambdaExpressionSyntax refLambda = SyntaxFactory.SimpleLambdaExpression(
					SyntaxFactory.Parameter(SyntaxFactory.Identifier("_")),
					expr);
				InvocationExpressionSyntax isRef = BuildItInvocation(
					itIdentifier, "IsRef", typeArgs,
					SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList([SyntaxFactory.Argument(refLambda),])));
				return SyntaxFactory.Argument(isRef).WithTriviaFrom(original);
			});
	}

	/// <summary>
	///     Matches <c>[Moq.]It.Ref&lt;T&gt;.IsAny</c> and returns the type argument list.
	/// </summary>
	private static bool TryExtractItRefIsAnyTypeArgs(ExpressionSyntax expression,
		out TypeArgumentListSyntax? typeArgs)
	{
		typeArgs = null;
		// expression must be: <receiver>.IsAny
		if (expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "IsAny", } isAnyAccess)
		{
			return false;
		}

		// receiver must be: It.Ref<T> or Moq.It.Ref<T>
		if (isAnyAccess.Expression is not MemberAccessExpressionSyntax refAccess)
		{
			return false;
		}

		if (refAccess.Name is not GenericNameSyntax { Identifier.Text: "Ref", TypeArgumentList: var tArgs, })
		{
			return false;
		}

		bool isIt = refAccess.Expression switch
		{
			IdentifierNameSyntax { Identifier.Text: "It", } => true,
			MemberAccessExpressionSyntax
			{
				Expression: IdentifierNameSyntax { Identifier.Text: "Moq", },
				Name.Identifier.Text: "It",
			} => true,
			_ => false,
		};

		if (!isIt)
		{
			return false;
		}

		typeArgs = tArgs;
		return true;
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

#pragma warning restore S3776 // Cognitive Complexity of methods should not be too high
#pragma warning restore S1192
