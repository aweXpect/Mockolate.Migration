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

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> verifyReplacements =
			FindAndBuildVerifyReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> clearReplacements =
			FindAndBuildClearReceivedCallsReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		Dictionary<AssignmentExpressionSyntax, InvocationExpressionSyntax> raiseReplacements =
			FindAndBuildRaiseReplacements(compilationUnit, semanticModel, mockSymbol, cancellationToken);

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> whenDoReplacements =
			FindAndBuildWhenDoReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		List<SyntaxNode> nodesToReplace = [substituteCall,];
		nodesToReplace.AddRange(setupReplacements.Keys);
		nodesToReplace.AddRange(verifyReplacements.Keys);
		nodesToReplace.AddRange(clearReplacements.Keys);
		nodesToReplace.AddRange(raiseReplacements.Keys);
		nodesToReplace.AddRange(whenDoReplacements.Keys);

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

					if (whenDoReplacements.TryGetValue(invocation, out InvocationExpressionSyntax? whenDoReplacement))
					{
						return whenDoReplacement;
					}
				}

				if (original is AssignmentExpressionSyntax assignment &&
				    raiseReplacements.TryGetValue(assignment, out InvocationExpressionSyntax? raiseReplacement))
				{
					return raiseReplacement;
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

		foreach (InvocationExpressionSyntax outerInvocation in allInvocations)
		{
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

				ArgumentListSyntax transformedArgs =
					TransformNSubstituteArgReferences(targetInvocation.ArgumentList, semanticModel, cancellationToken);

				MemberAccessExpressionSyntax setupAccess = BuildSetupAccess(
					targetMemberAccess.Expression, targetMemberAccess.Name);
				InvocationExpressionSyntax setupInvocation = SyntaxFactory.InvocationExpression(setupAccess, transformedArgs)
					.WithTriviaFrom(targetInvocation);

				result[targetInvocation] = setupInvocation;
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
			}
		}

		return result;
	}

	/// <summary>
	///     Translates <c>sub.When(x =&gt; x.Method(args)).Do(callback)</c> to
	///     <c>sub.Mock.Setup.Method(args).Do(callback)</c>, and
	///     <c>sub.When(x =&gt; x.Method(args)).DoNotCallBase()</c> to
	///     <c>sub.Mock.Setup.Method(args).SkippingBaseClass()</c>.
	/// </summary>
	private static Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> FindAndBuildWhenDoReplacements(
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

		foreach (InvocationExpressionSyntax trailingInvocation in allInvocations)
		{
			if (trailingInvocation.Expression is not MemberAccessExpressionSyntax trailingAccess)
			{
				continue;
			}

			string trailingMethod = trailingAccess.Name.Identifier.Text;
			if (trailingMethod is not ("Do" or "DoNotCallBase"))
			{
				continue;
			}

			// Receiver of .Do(...) / .DoNotCallBase() must be a When(...) invocation on the tracked mock.
			if (trailingAccess.Expression is not InvocationExpressionSyntax whenInvocation ||
			    whenInvocation.Expression is not MemberAccessExpressionSyntax whenAccess ||
			    whenAccess.Name.Identifier.Text != "When")
			{
				continue;
			}

			if (!IsTrackedMockReceiver(whenAccess.Expression, semanticModel, mockSymbol, cancellationToken))
			{
				continue;
			}

			// When(lambda) — extract the method call from the lambda body.
			if (whenInvocation.ArgumentList.Arguments.Count != 1 ||
			    whenInvocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda ||
			    lambda.Body is not InvocationExpressionSyntax lambdaBody ||
			    lambdaBody.Expression is not MemberAccessExpressionSyntax lambdaMemberAccess)
			{
				continue;
			}

			ArgumentListSyntax transformedArgs = TransformNSubstituteArgReferences(lambdaBody.ArgumentList, semanticModel, cancellationToken);
			MemberAccessExpressionSyntax setupAccess = BuildSetupAccess(whenAccess.Expression, lambdaMemberAccess.Name.WithoutTrivia());
			InvocationExpressionSyntax setupCall = SyntaxFactory.InvocationExpression(setupAccess, transformedArgs);

			(string trailingName, ArgumentListSyntax trailingArgs) = trailingMethod == "DoNotCallBase"
				? ("SkippingBaseClass", trailingInvocation.ArgumentList)
				: ("Do", trailingInvocation.ArgumentList);

			MemberAccessExpressionSyntax trailingMember = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				setupCall,
				SyntaxFactory.IdentifierName(trailingName));

			result[trailingInvocation] = SyntaxFactory.InvocationExpression(trailingMember, trailingArgs)
				.WithTriviaFrom(trailingInvocation);
		}

		return result;
	}

	private static Dictionary<AssignmentExpressionSyntax, InvocationExpressionSyntax> FindAndBuildRaiseReplacements(
		CompilationUnitSyntax compilationUnit,
		SemanticModel? semanticModel,
		ISymbol? mockSymbol,
		CancellationToken cancellationToken)
	{
		if (semanticModel is null || mockSymbol is null)
		{
			return [];
		}

		Dictionary<AssignmentExpressionSyntax, InvocationExpressionSyntax> result = [];

		foreach (AssignmentExpressionSyntax assignment in compilationUnit.DescendantNodes().OfType<AssignmentExpressionSyntax>())
		{
			if (!assignment.IsKind(SyntaxKind.AddAssignmentExpression))
			{
				continue;
			}

			if (assignment.Left is not MemberAccessExpressionSyntax eventAccess ||
			    assignment.Right is not InvocationExpressionSyntax raiseInvocation)
			{
				continue;
			}

			if (!IsTrackedMockReceiver(eventAccess.Expression, semanticModel, mockSymbol, cancellationToken))
			{
				continue;
			}

			if (semanticModel.GetSymbolInfo(eventAccess, cancellationToken).Symbol is not IEventSymbol)
			{
				continue;
			}

			if (raiseInvocation.Expression is not MemberAccessExpressionSyntax raiseAccess)
			{
				continue;
			}

			if (semanticModel.GetSymbolInfo(raiseInvocation, cancellationToken).Symbol is not IMethodSymbol raiseMethodSymbol ||
			    raiseMethodSymbol.ContainingType?.Name != "Raise" ||
			    raiseMethodSymbol.ContainingType.ContainingNamespace?.ToDisplayString() != "NSubstitute")
			{
				continue;
			}

			string raiseMethod = raiseAccess.Name.Identifier.Text;
			ArgumentListSyntax raiseArgs = BuildRaiseArguments(raiseInvocation.ArgumentList, raiseMethod);

			MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				eventAccess.Expression,
				SyntaxFactory.IdentifierName("Mock"));
			MemberAccessExpressionSyntax raiseMember = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				mockAccess,
				SyntaxFactory.IdentifierName("Raise"));
			MemberAccessExpressionSyntax raiseEventName = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				raiseMember,
				eventAccess.Name.WithoutTrivia());

			result[assignment] = SyntaxFactory.InvocationExpression(raiseEventName, raiseArgs)
				.WithTriviaFrom(assignment);
		}

		return result;
	}

	/// <summary>
	///     Translates the argument list of an NSubstitute <c>Raise.X(...)</c> call into the corresponding
	///     <c>Mock.Raise.EventName(...)</c> argument list.
	/// </summary>
	private static ArgumentListSyntax BuildRaiseArguments(ArgumentListSyntax raiseArgs, string raiseMethod)
	{
		// Raise.Event<TDelegate>(args...) — non-EventHandler delegates, just forward the args.
		// Raise.EventWith(args)       — single arg means EventArgs only (sender omitted, defaults to null).
		// Raise.EventWith(sender, ea) — two args, pass through.
		// Raise.Event() / Raise.EventWith() — empty, default to (null, EventArgs.Empty) for EventHandler.
		if (raiseMethod is "Event")
		{
			if (raiseArgs.Arguments.Count == 0)
			{
				return SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
				[
					SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
					EventArgsEmptyArgument(),
				]));
			}

			return raiseArgs;
		}

		if (raiseMethod is "EventWith")
		{
			if (raiseArgs.Arguments.Count == 0)
			{
				return SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
				[
					SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
					EventArgsEmptyArgument(),
				]));
			}

			if (raiseArgs.Arguments.Count == 1)
			{
				return SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
				[
					SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
					raiseArgs.Arguments[0],
				]));
			}
		}

		return raiseArgs;
	}

	// Fully qualified so the rewrite compiles even when the source file does not have `using System;`.
	private static ArgumentSyntax EventArgsEmptyArgument() =>
		SyntaxFactory.Argument(SyntaxFactory.ParseExpression("global::System.EventArgs.Empty"));

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

			if (invocation.ArgumentList.Arguments.Count != 0)
			{
				continue;
			}

			if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol methodSymbol)
			{
				continue;
			}

			IMethodSymbol originalMethod = methodSymbol.ReducedFrom ?? methodSymbol;
			if (originalMethod.ContainingType?.Name != "SubstituteExtensions" ||
			    originalMethod.ContainingType.ContainingNamespace?.ToDisplayString() != "NSubstitute")
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
			if (receivedMethod is not ("Received" or "ReceivedWithAnyArgs" or "DidNotReceive" or "DidNotReceiveWithAnyArgs"))
			{
				continue;
			}

			if (!IsTrackedMockReceiver(receiverAccess.Expression, semanticModel, mockSymbol, cancellationToken))
			{
				continue;
			}

			ExpressionSyntax mockReceiver = receiverAccess.Expression;
			ArgumentListSyntax transformedArgs =
				TransformNSubstituteArgReferences(outerInvocation.ArgumentList, semanticModel, cancellationToken);
			SimpleNameSyntax methodNameSyntax = outerAccess.Name;

			MemberAccessExpressionSyntax verifyAccess = BuildVerifyAccess(mockReceiver, methodNameSyntax);
			InvocationExpressionSyntax verifyInvocation = SyntaxFactory.InvocationExpression(verifyAccess, transformedArgs);

			InvocationExpressionSyntax verifyTarget = receivedMethod is "ReceivedWithAnyArgs" or "DidNotReceiveWithAnyArgs"
				? SyntaxFactory.InvocationExpression(
					SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						verifyInvocation,
						SyntaxFactory.IdentifierName("AnyParameters")),
					SyntaxFactory.ArgumentList())
				: verifyInvocation;

			bool isNegative = receivedMethod is "DidNotReceive" or "DidNotReceiveWithAnyArgs";
			InvocationExpressionSyntax suffix = BuildVerifySuffix(verifyTarget, isNegative, receiverCall.ArgumentList);

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
		bool isNegative, ArgumentListSyntax receivedArgs)
	{
		// Negative verifications (DidNotReceive / DidNotReceiveWithAnyArgs) → .Never().
		// Positive verifications use receivedArgs (the args originally passed to Received(...) ) to choose the count:
		//   no arguments → .AtLeastOnce(); a single literal 1 → .Once(); otherwise → .Exactly(receivedArgs).
		if (isNegative)
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
	///     mirrors. Uses the semantic model so fully-qualified usages
	///     (<c>NSubstitute.Arg.Any&lt;T&gt;()</c>, aliased imports, etc.) are recognised too.
	/// </summary>
	private static ArgumentListSyntax TransformNSubstituteArgReferences(ArgumentListSyntax args,
		SemanticModel semanticModel, CancellationToken cancellationToken) =>
		args.ReplaceNodes(
			args.DescendantNodes().OfType<InvocationExpressionSyntax>()
				.Where(invocation => IsNSubstituteArgCall(invocation, semanticModel, cancellationToken))
				.ToArray(),
			TransformNSubstituteArgInvocation);

	private static bool IsNSubstituteArgCall(InvocationExpressionSyntax invocation,
		SemanticModel semanticModel, CancellationToken cancellationToken)
	{
		if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol methodSymbol)
		{
			return false;
		}

		// Walk up nested types so both Arg.X(...) and Arg.Compat.X(...) resolve to NSubstitute.Arg.
		for (INamedTypeSymbol? containingType = methodSymbol.ContainingType;
		     containingType is not null;
		     containingType = containingType.ContainingType)
		{
			if (containingType.Name == "Arg" &&
			    containingType.ContainingNamespace?.ToDisplayString() == "NSubstitute")
			{
				return true;
			}
		}

		return false;
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

#pragma warning restore S3776 // Cognitive Complexity of methods should not be too high
#pragma warning restore S1192
