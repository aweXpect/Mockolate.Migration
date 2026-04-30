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

		InvocationExpressionSyntax? substituteCall = FindSubstituteCreationCall(expressionSyntax);
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

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> verifyReplacements =
			FindAndBuildVerifyReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> clearReplacements =
			FindAndBuildClearReceivedCallsReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		List<SyntaxNode> nodesToReplace = [substituteCall,];
		nodesToReplace.AddRange(setupReplacements.Keys);
		nodesToReplace.AddRange(verifyReplacements.Keys);
		nodesToReplace.AddRange(clearReplacements.Keys);

		compilationUnit = compilationUnit.ReplaceNodes(
			nodesToReplace,
			(original, _) =>
			{
				if (original == substituteCall)
				{
					return creationReplacement.WithTriviaFrom(substituteCall);
				}

				if (setupReplacements.TryGetValue(original, out SyntaxNode? setupReplacement))
				{
					return setupReplacement;
				}

				if (original is InvocationExpressionSyntax invocation)
				{
					if (verifyReplacements.TryGetValue(invocation, out InvocationExpressionSyntax? verifyReplacement))
					{
						return verifyReplacement;
					}

					if (clearReplacements.TryGetValue(invocation, out InvocationExpressionSyntax? clearReplacement))
					{
						return clearReplacement;
					}
				}

				return original;
			});

		bool hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == "Mockolate");
		if (!hasUsing)
		{
			UsingDirectiveSyntax usingDirective = BuildUsingDirective(compilationUnit, "Mockolate");
			compilationUnit = compilationUnit.AddUsings(usingDirective);
		}

		if (verifyReplacements.Count > 0)
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

	private static InvocationExpressionSyntax? FindSubstituteCreationCall(ExpressionSyntax expressionSyntax)
	{
		foreach (InvocationExpressionSyntax invocation in expressionSyntax.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
		{
			if (IsSubstituteCreationCall(invocation))
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

	private static Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> FindAndBuildClearReceivedCallsReplacements(
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
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
			    memberAccess.Name.Identifier.Text != "ClearReceivedCalls")
			{
				continue;
			}

			if (!IsTrackedMockReceiver(memberAccess.Expression, semanticModel, mockSymbol, cancellationToken))
			{
				continue;
			}

			MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				memberAccess.Expression,
				SyntaxFactory.IdentifierName("Mock"));
			MemberAccessExpressionSyntax clearAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				mockAccess,
				SyntaxFactory.IdentifierName("ClearAllInteractions"));

			result[invocation] = SyntaxFactory.InvocationExpression(clearAccess, SyntaxFactory.ArgumentList())
				.WithTriviaFrom(invocation);
		}

		return result;
	}

	private static Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> FindAndBuildVerifyReplacements(
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

		foreach (InvocationExpressionSyntax outerInvocation in allInvocations)
		{
			if (outerInvocation.Expression is not MemberAccessExpressionSyntax outerAccess)
			{
				continue;
			}

			// outerInvocation is `something.MethodName(args)`; receiver `something` should be `sub.Received(...)`
			// (or DidNotReceive). The receiver of `sub.Received()` is the tracked mock symbol.
			if (outerAccess.Expression is not InvocationExpressionSyntax receiverCall ||
			    receiverCall.Expression is not MemberAccessExpressionSyntax receiverAccess)
			{
				continue;
			}

			string receivedMethod = receiverAccess.Name.Identifier.Text;
			if (receivedMethod is not ("Received" or "DidNotReceive"))
			{
				continue;
			}

			if (!IsTrackedMockReceiver(receiverAccess.Expression, semanticModel, mockSymbol, cancellationToken))
			{
				continue;
			}

			ExpressionSyntax mockReceiver = receiverAccess.Expression;
			ArgumentListSyntax transformedArgs = TransformNSubstituteArgReferences(outerInvocation.ArgumentList);
			SimpleNameSyntax methodNameSyntax = outerAccess.Name;

			MemberAccessExpressionSyntax verifyAccess = BuildVerifyAccess(mockReceiver, methodNameSyntax);
			InvocationExpressionSyntax verifyInvocation = SyntaxFactory.InvocationExpression(verifyAccess, transformedArgs);

			InvocationExpressionSyntax suffix = BuildVerifySuffix(verifyInvocation, receivedMethod, receiverCall.ArgumentList);

			result[outerInvocation] = suffix.WithTriviaFrom(outerInvocation);
		}

		return result;
	}

	private static MemberAccessExpressionSyntax BuildVerifyAccess(ExpressionSyntax receiver, SimpleNameSyntax memberName)
	{
		MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
			SyntaxKind.SimpleMemberAccessExpression,
			receiver,
			SyntaxFactory.IdentifierName("Mock"));
		MemberAccessExpressionSyntax verifyMember = SyntaxFactory.MemberAccessExpression(
			SyntaxKind.SimpleMemberAccessExpression,
			mockAccess,
			SyntaxFactory.IdentifierName("Verify"));
		return SyntaxFactory.MemberAccessExpression(
			SyntaxKind.SimpleMemberAccessExpression,
			verifyMember,
			memberName);
	}

	private static InvocationExpressionSyntax BuildVerifySuffix(InvocationExpressionSyntax verifyInvocation,
		string receivedMethod, ArgumentListSyntax receivedArgs)
	{
		// DidNotReceive() → .Never(); Received() → .AtLeastOnce(); Received(n) → .Exactly(n) or .Once() when n is 1.
		if (receivedMethod == "DidNotReceive")
		{
			return AppendCountCall(verifyInvocation, "Never", SyntaxFactory.ArgumentList());
		}

		if (receivedArgs.Arguments.Count == 0)
		{
			return AppendCountCall(verifyInvocation, "AtLeastOnce", SyntaxFactory.ArgumentList());
		}

		// Received(n): when n is the literal integer 1 we collapse to Once(); otherwise pass through to Exactly(n).
		if (receivedArgs.Arguments.Count == 1 &&
		    receivedArgs.Arguments[0].Expression is LiteralExpressionSyntax literal &&
		    literal.Token.Value is 1)
		{
			return AppendCountCall(verifyInvocation, "Once", SyntaxFactory.ArgumentList());
		}

		return AppendCountCall(verifyInvocation, "Exactly", receivedArgs);
	}

	private static InvocationExpressionSyntax AppendCountCall(InvocationExpressionSyntax verifyInvocation,
		string methodName, ArgumentListSyntax argList) =>
		SyntaxFactory.InvocationExpression(
			SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				verifyInvocation,
				SyntaxFactory.IdentifierName(methodName)),
			argList);

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
				initializer: null);

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

		// Match any leading whitespace style of the existing first using to keep the diff tidy.
		if (compilationUnit.Usings.Count > 0)
		{
			directive = directive.WithLeadingTrivia(compilationUnit.Usings[0].GetLeadingTrivia());
		}

		return directive.WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));
	}
}
