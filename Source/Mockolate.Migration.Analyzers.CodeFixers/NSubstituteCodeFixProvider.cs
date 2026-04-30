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
///     A code fix provider that migrates NSubstitute to Mockolate.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NSubstituteCodeFixProvider))]
[Shared]
public class NSubstituteCodeFixProvider() : AssertionCodeFixProvider(Rules.NSubstituteRule)
{
	private static readonly HashSet<string> SetupConfiguratorMethods =
	[
		"Returns",
		"ReturnsForAnyArgs",
		"ReturnsNull",
		"ReturnsNullForAnyArgs",
		"Throws",
		"ThrowsForAnyArgs",
		"ThrowsAsync",
		"ThrowsAsyncForAnyArgs",
		"AndDoes",
	];

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

		InvocationExpressionSyntax? substituteCall = FindSubstituteCreationCall(expressionSyntax, semanticModel, cancellationToken);
		if (substituteCall is null)
		{
			return document;
		}

		ExpressionSyntax? creationReplacement = BuildCreationReplacement(substituteCall);
		if (creationReplacement is null)
		{
			return document;
		}

		ISymbol? mockSymbol = GetDeclaredMockSymbol(semanticModel, substituteCall, cancellationToken);
		IReadOnlyList<InvocationExpressionSyntax> allInvocations =
			compilationUnit.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

		Dictionary<SyntaxNode, SyntaxNode> setupReplacements =
			FindAndBuildSetupReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		List<SyntaxNode> nodesToReplace = [substituteCall,];
		nodesToReplace.AddRange(setupReplacements.Keys);

		compilationUnit = compilationUnit.ReplaceNodes(
			nodesToReplace,
			(original, _) =>
			{
				if (original == substituteCall)
				{
					return creationReplacement.WithTriviaFrom(substituteCall);
				}

				if (setupReplacements.TryGetValue(original, out SyntaxNode? replacement))
				{
					return replacement;
				}

				return original;
			});

		bool hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == "Mockolate");
		if (!hasUsing)
		{
			UsingDirectiveSyntax usingDirective = BuildUsingDirective(compilationUnit, "Mockolate");
			compilationUnit = compilationUnit.AddUsings(usingDirective);
		}

		return document.WithSyntaxRoot(compilationUnit);
	}

	private static InvocationExpressionSyntax? FindSubstituteCreationCall(
		ExpressionSyntax expressionSyntax,
		SemanticModel? semanticModel,
		CancellationToken cancellationToken)
	{
		foreach (InvocationExpressionSyntax invocation in expressionSyntax.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			{
				continue;
			}

			if (memberAccess.Name.Identifier.Text is not ("For" or "ForPartsOf" or "ForTypeForwardingTo"))
			{
				continue;
			}

			// Use the semantic model to match NSubstitute.Substitute regardless of how it's qualified
			// (Substitute.For<T>(), NSubstitute.Substitute.For<T>(), global::NSubstitute.Substitute.For<T>(), aliases, etc.).
			if (semanticModel?.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol methodSymbol &&
			    methodSymbol.ContainingType?.Name == "Substitute" &&
			    methodSymbol.ContainingType.ContainingNamespace?.ToDisplayString() == "NSubstitute")
			{
				return invocation;
			}
		}

		return null;
	}

	private static bool IsSubstituteCreationCall(InvocationExpressionSyntax invocation) =>
		invocation.Expression is MemberAccessExpressionSyntax
		{
			Expression: IdentifierNameSyntax { Identifier.Text: "Substitute", },
			Name: var name,
		} && name.Identifier.Text is "For" or "ForPartsOf" or "ForTypeForwardingTo";

	private static ISymbol? GetDeclaredMockSymbol(SemanticModel? semanticModel,
		InvocationExpressionSyntax substituteCall, CancellationToken cancellationToken)
	{
		if (semanticModel is null)
		{
			return null;
		}

		// The substitute call may be wrapped: var sub = Substitute.For<T>().Implementing<T2>(); we still want
		// the variable declarator above. Walk up through expressions to find the EqualsValueClause.
		SyntaxNode current = substituteCall;
		while (current.Parent is ExpressionSyntax)
		{
			current = current.Parent;
		}

		return current.Parent switch
		{
			EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator, }
				=> semanticModel.GetDeclaredSymbol(declarator, cancellationToken),
			EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax prop, }
				=> semanticModel.GetDeclaredSymbol(prop, cancellationToken),
			_ => null,
		};
	}

	private static Dictionary<SyntaxNode, SyntaxNode> FindAndBuildSetupReplacements(
		IReadOnlyList<InvocationExpressionSyntax> allInvocations,
		SemanticModel? semanticModel,
		ISymbol? mockSymbol,
		CancellationToken cancellationToken)
	{
		if (semanticModel is null || mockSymbol is null)
		{
			return [];
		}

		Dictionary<SyntaxNode, SyntaxNode> result = [];
		HashSet<SyntaxNode> alreadyAccountedFor = [];

		foreach (InvocationExpressionSyntax outerInvocation in allInvocations)
		{
			if (alreadyAccountedFor.Contains(outerInvocation))
			{
				continue;
			}

			if (outerInvocation.Expression is not MemberAccessExpressionSyntax outerAccess ||
			    !SetupConfiguratorMethods.Contains(outerAccess.Name.Identifier.Text))
			{
				continue;
			}

			// The receiver of the configurator (e.g. .Returns) is either
			//   sub.Method(args)             — pattern A
			//   sub.Property                 — pattern B
			ExpressionSyntax receiver = outerAccess.Expression;

			if (receiver is InvocationExpressionSyntax targetInvocation &&
			    targetInvocation.Expression is MemberAccessExpressionSyntax targetMemberAccess)
			{
				if (!IsTrackedMockReceiver(targetMemberAccess.Expression, semanticModel, mockSymbol, cancellationToken))
				{
					continue;
				}

				ArgumentListSyntax transformedArgs = TransformNSubstituteArgReferences(targetInvocation.ArgumentList);

				MemberAccessExpressionSyntax setupAccess = BuildSetupAccess(
					targetMemberAccess.Expression, targetMemberAccess.Name);
				InvocationExpressionSyntax setupInvocation = SyntaxFactory.InvocationExpression(setupAccess, transformedArgs)
					.WithTriviaFrom(targetInvocation);

				result[targetInvocation] = setupInvocation;
				alreadyAccountedFor.Add(targetInvocation);
				continue;
			}

			if (receiver is MemberAccessExpressionSyntax targetPropertyAccess)
			{
				if (!IsTrackedMockReceiver(targetPropertyAccess.Expression, semanticModel, mockSymbol, cancellationToken))
				{
					continue;
				}

				MemberAccessExpressionSyntax setupAccess = BuildSetupAccess(
					targetPropertyAccess.Expression, targetPropertyAccess.Name);

				result[targetPropertyAccess] = setupAccess.WithTriviaFrom(targetPropertyAccess);
				alreadyAccountedFor.Add(targetPropertyAccess);
			}
		}

		return result;
	}

	/// <summary>
	///     Returns <see langword="true" /> when <paramref name="expression" /> ultimately resolves to the tracked
	///     mock symbol — either directly or via a chain of property/field accesses (auto-mocked nested members).
	/// </summary>
	private static bool IsTrackedMockReceiver(ExpressionSyntax expression,
		SemanticModel semanticModel, ISymbol mockSymbol, CancellationToken cancellationToken)
	{
		ExpressionSyntax current = expression;
		while (true)
		{
			SymbolInfo info = semanticModel.GetSymbolInfo(current, cancellationToken);
			if (SymbolEqualityComparer.Default.Equals(info.Symbol, mockSymbol))
			{
				return true;
			}

			if (current is MemberAccessExpressionSyntax memberAccess)
			{
				current = memberAccess.Expression;
				continue;
			}

			return false;
		}
	}

	private static MemberAccessExpressionSyntax BuildSetupAccess(ExpressionSyntax receiver, SimpleNameSyntax memberName)
	{
		MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
			SyntaxKind.SimpleMemberAccessExpression,
			receiver,
			SyntaxFactory.IdentifierName("Mock"));
		MemberAccessExpressionSyntax setupAccess = SyntaxFactory.MemberAccessExpression(
			SyntaxKind.SimpleMemberAccessExpression,
			mockAccess,
			SyntaxFactory.IdentifierName("Setup"));
		return SyntaxFactory.MemberAccessExpression(
			SyntaxKind.SimpleMemberAccessExpression,
			setupAccess,
			memberName);
	}

	/// <summary>
	///     Translates NSubstitute argument matchers to their Mockolate equivalents anywhere inside the supplied
	///     argument list. Currently handles <c>Arg.Any&lt;T&gt;</c>, <c>Arg.Is</c>, and the <c>Arg.Compat</c>
	///     mirrors.
	/// </summary>
	private static ArgumentListSyntax TransformNSubstituteArgReferences(ArgumentListSyntax args) =>
		args.ReplaceNodes(
			args.DescendantNodes().OfType<InvocationExpressionSyntax>()
				.Where(IsNSubstituteArgCall)
				.ToArray(),
			TransformNSubstituteArgInvocation);

	private static bool IsNSubstituteArgCall(InvocationExpressionSyntax invocation)
	{
		// Direct: Arg.X<T>(...)
		if (invocation.Expression is MemberAccessExpressionSyntax
		    {
			    Expression: IdentifierNameSyntax { Identifier.Text: "Arg", },
		    })
		{
			return true;
		}

		// Compat: Arg.Compat.X<T>(...)
		return invocation.Expression is MemberAccessExpressionSyntax
		{
			Expression: MemberAccessExpressionSyntax
			{
				Expression: IdentifierNameSyntax { Identifier.Text: "Arg", },
				Name.Identifier.Text: "Compat",
			},
		};
	}

	private static SyntaxNode TransformNSubstituteArgInvocation(InvocationExpressionSyntax original,
		InvocationExpressionSyntax rewritten)
	{
		if (rewritten.Expression is not MemberAccessExpressionSyntax memberAccess)
		{
			return rewritten;
		}

		string methodName = memberAccess.Name.Identifier.Text;
		TypeArgumentListSyntax? typeArgs = (memberAccess.Name as GenericNameSyntax)?.TypeArgumentList;

		IdentifierNameSyntax itIdentifier = SyntaxFactory.IdentifierName("It");

		switch (methodName)
		{
			case "Any":
				// Arg.Any<T>() → It.IsAny<T>()
				return BuildItInvocation(itIdentifier, "IsAny", typeArgs, SyntaxFactory.ArgumentList())
					.WithTriviaFrom(original);

			case "AnyType":
				// Arg.AnyType — used as a type marker, not an invocation. Skip.
				return rewritten;

			case "Is":
				// Arg.Is<T>(predicate)         → It.Satisfies<T>(predicate)
				// Arg.Is<T>(value) / Arg.Is(v) → It.Is<T>(value) (or just inline value)
				if (rewritten.ArgumentList.Arguments.Count == 1 &&
				    rewritten.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax)
				{
					return BuildItInvocation(itIdentifier, "Satisfies", typeArgs, rewritten.ArgumentList)
						.WithTriviaFrom(original);
				}

				return BuildItInvocation(itIdentifier, "Is", typeArgs, rewritten.ArgumentList)
					.WithTriviaFrom(original);

			default:
				return rewritten;
		}
	}

	private static InvocationExpressionSyntax BuildItInvocation(IdentifierNameSyntax itIdentifier, string methodName,
		TypeArgumentListSyntax? typeArgs, ArgumentListSyntax argList)
	{
		SimpleNameSyntax method = typeArgs is null
			? SyntaxFactory.IdentifierName(methodName)
			: SyntaxFactory.GenericName(SyntaxFactory.Identifier(methodName)).WithTypeArgumentList(typeArgs);
		return SyntaxFactory.InvocationExpression(
			SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				itIdentifier,
				method),
			argList);
	}

	/// <summary>
	///     Translates the NSubstitute creation call to a Mockolate creation chain. Returns <see langword="null" />
	///     when the call cannot be migrated.
	/// </summary>
	private static ExpressionSyntax? BuildCreationReplacement(InvocationExpressionSyntax substituteCall)
	{
		if (substituteCall.Expression is not MemberAccessExpressionSyntax memberAccess)
		{
			return null;
		}

		string methodName = memberAccess.Name.Identifier.Text;
		if (methodName is not ("For" or "ForPartsOf" or "ForTypeForwardingTo"))
		{
			return null;
		}

		if (memberAccess.Name is not GenericNameSyntax generic ||
		    generic.TypeArgumentList.Arguments.Count == 0)
		{
			return null;
		}

		SeparatedSyntaxList<TypeSyntax> typeArgs = generic.TypeArgumentList.Arguments;
		ArgumentListSyntax args = substituteCall.ArgumentList;

		if (methodName == "ForTypeForwardingTo")
		{
			// Substitute.ForTypeForwardingTo<TInterface, TClass>(ctorArgs)
			//   -> TInterface.CreateMock().Wrapping(new TClass(ctorArgs))
			if (typeArgs.Count != 2)
			{
				return null;
			}

			InvocationExpressionSyntax createMock = SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					typeArgs[0].WithoutTrivia(),
					SyntaxFactory.IdentifierName("CreateMock")));

			ObjectCreationExpressionSyntax newClass = SyntaxFactory.ObjectCreationExpression(
				typeArgs[1].WithoutTrivia(),
				args,
				null);

			return SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					createMock,
					SyntaxFactory.IdentifierName("Wrapping")),
				SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(newClass))));
		}

		// For / ForPartsOf: chain Implementing<T2>(), Implementing<T3>(), ... after the first type
		ExpressionSyntax current = SyntaxFactory.InvocationExpression(
			SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				typeArgs[0].WithoutTrivia(),
				SyntaxFactory.IdentifierName("CreateMock")),
			args);

		for (int i = 1; i < typeArgs.Count; i++)
		{
			current = SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					current,
					SyntaxFactory.GenericName(SyntaxFactory.Identifier("Implementing"))
						.WithTypeArgumentList(
							SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(typeArgs[i].WithoutTrivia())))));
		}

		return current;
	}

	private static UsingDirectiveSyntax BuildUsingDirective(CompilationUnitSyntax compilationUnit, string namespaceName)
	{
		UsingDirectiveSyntax directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName));

		// Match the trivia style of the existing first using to keep the diff tidy and preserve line endings.
		if (compilationUnit.Usings.Count > 0)
		{
			UsingDirectiveSyntax existing = compilationUnit.Usings[0];
			return directive
				.WithLeadingTrivia(existing.GetLeadingTrivia())
				.WithTrailingTrivia(existing.GetTrailingTrivia());
		}

		// No existing usings — emit a blank-line separator so the new using is followed by an empty line
		// before the first member, matching standard C# layout.
		string endOfLine = DetectLineEnding(compilationUnit);
		return directive.WithTrailingTrivia(SyntaxFactory.EndOfLine(endOfLine), SyntaxFactory.EndOfLine(endOfLine));
	}

	private static string DetectLineEnding(SyntaxNode root)
	{
		foreach (SyntaxTrivia trivia in root.DescendantTrivia(descendIntoTrivia: true))
		{
			if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
			{
				string text = trivia.ToFullString();
				if (text.Length > 0)
				{
					return text;
				}
			}
		}

		return "\n";
	}
}
