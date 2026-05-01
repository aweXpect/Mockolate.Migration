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

		Dictionary<AssignmentExpressionSyntax, InvocationExpressionSyntax> propertyVerifyReplacements =
			FindAndBuildPropertyVerifyReplacements(compilationUnit, semanticModel, mockSymbol, cancellationToken);

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
		nodesToReplace.AddRange(propertyVerifyReplacements.Keys);

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

				if (original is AssignmentExpressionSyntax assignment)
				{
					if (raiseReplacements.TryGetValue(assignment, out InvocationExpressionSyntax? raiseReplacement))
					{
						return raiseReplacement;
					}

					if (propertyVerifyReplacements.TryGetValue(assignment, out InvocationExpressionSyntax? propVerifyReplacement))
					{
						return propVerifyReplacement;
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

		if (verifyReplacements.Count > 0 || propertyVerifyReplacements.Count > 0)
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

			string configuratorMethod = outerAccess.Name.Identifier.Text;

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

				if (TryBuildSequentialOuter(outerInvocation, configuratorMethod, setupInvocation,
					    out InvocationExpressionSyntax? sequentialReplacement))
				{
					// Multi-arg Returns/Throws/etc. — replace the WHOLE outer expression so the call list expands
					// into a chain of single-arg calls (Mockolate has no multi-arg overloads).
					result[outerInvocation] = sequentialReplacement;
					alreadyAccountedFor.Add(outerInvocation);
					alreadyAccountedFor.Add(targetInvocation);
				}
				else
				{
					result[targetInvocation] = setupInvocation;
					alreadyAccountedFor.Add(targetInvocation);
				}

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

				if (TryBuildSequentialOuter(outerInvocation, configuratorMethod, setupAccess,
					    out InvocationExpressionSyntax? sequentialReplacement))
				{
					result[outerInvocation] = sequentialReplacement;
					alreadyAccountedFor.Add(outerInvocation);
					alreadyAccountedFor.Add(targetPropertyAccess);
				}
				else
				{
					result[targetPropertyAccess] = setupAccess.WithTriviaFrom(targetPropertyAccess);
					alreadyAccountedFor.Add(targetPropertyAccess);
				}
			}
		}

		return result;
	}

	/// <summary>
	///     When <paramref name="configuratorMethod" /> is <c>Returns</c> or <c>Throws</c> with more than one argument,
	///     splits it into a chain of single-argument calls (Mockolate has no multi-arg overload). Returns
	///     <see langword="false" /> when no rewrite is needed.
	/// </summary>
	private static bool TryBuildSequentialOuter(InvocationExpressionSyntax outerInvocation,
		string configuratorMethod, ExpressionSyntax setupReceiver,
		out InvocationExpressionSyntax? replacement)
	{
		replacement = null;

		if (configuratorMethod is not ("Returns" or "Throws"))
		{
			return false;
		}

		if (outerInvocation.ArgumentList.Arguments.Count <= 1)
		{
			return false;
		}

		ExpressionSyntax current = setupReceiver;
		foreach (ArgumentSyntax arg in outerInvocation.ArgumentList.Arguments)
		{
			current = SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					current,
					SyntaxFactory.IdentifierName(configuratorMethod)),
				SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(arg.WithoutTrivia())));
		}

		replacement = ((InvocationExpressionSyntax)current).WithTriviaFrom(outerInvocation);
		return true;
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

			ArgumentListSyntax transformedArgs = TransformNSubstituteArgReferences(lambdaBody.ArgumentList);
			MemberAccessExpressionSyntax setupAccess = BuildSetupAccess(whenAccess.Expression, lambdaMemberAccess.Name.WithoutTrivia());
			InvocationExpressionSyntax setupCall = SyntaxFactory.InvocationExpression(setupAccess, transformedArgs);

			(string trailingName, ArgumentListSyntax trailingArgs) = trailingMethod == "DoNotCallBase"
				? ("SkippingBaseClass", SyntaxFactory.ArgumentList())
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

			if (raiseInvocation.Expression is not MemberAccessExpressionSyntax raiseAccess ||
			    raiseAccess.Expression is not IdentifierNameSyntax { Identifier.Text: "Raise", })
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
					SyntaxFactory.Argument(SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						SyntaxFactory.IdentifierName("EventArgs"),
						SyntaxFactory.IdentifierName("Empty"))),
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
					SyntaxFactory.Argument(SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						SyntaxFactory.IdentifierName("EventArgs"),
						SyntaxFactory.IdentifierName("Empty"))),
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

			return raiseArgs;
		}

		return raiseArgs;
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

	/// <summary>
	///     Translates property-style verifications:
	///     <list type="bullet">
	///         <item><c>_ = sub.Received().Prop</c> → <c>sub.Mock.Verify.Prop.Got().AtLeastOnce()</c></item>
	///         <item><c>sub.Received().Prop = v</c> → <c>sub.Mock.Verify.Prop.Set(v).AtLeastOnce()</c></item>
	///     </list>
	///     <c>Received(n)</c> / <c>DidNotReceive()</c> map to <c>Exactly(n)</c>/<c>Once()</c>/<c>Never()</c> in the same way as method-style.
	/// </summary>
	private static Dictionary<AssignmentExpressionSyntax, InvocationExpressionSyntax> FindAndBuildPropertyVerifyReplacements(
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
			if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
			{
				continue;
			}

			// Got pattern: `_ = sub.Received().Prop`
			if (assignment.Left is IdentifierNameSyntax { Identifier.Text: "_", } &&
			    TryExtractReceivedPropertyAccess(assignment.Right, semanticModel, mockSymbol, cancellationToken,
				    out ExpressionSyntax? gotMockReceiver, out SimpleNameSyntax? gotPropertyName,
				    out string? gotReceivedMethod, out ArgumentListSyntax? gotReceivedArgs))
			{
				result[assignment] = BuildPropertyVerifyChain(gotMockReceiver, gotPropertyName, "Got",
						SyntaxFactory.ArgumentList(), gotReceivedMethod, gotReceivedArgs)
					.WithTriviaFrom(assignment);
				continue;
			}

			// Set pattern: `sub.Received().Prop = value`
			if (TryExtractReceivedPropertyAccess(assignment.Left, semanticModel, mockSymbol, cancellationToken,
				    out ExpressionSyntax? setMockReceiver, out SimpleNameSyntax? setPropertyName,
				    out string? setReceivedMethod, out ArgumentListSyntax? setReceivedArgs))
			{
				ArgumentListSyntax setArgs = SyntaxFactory.ArgumentList(
					SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(assignment.Right.WithoutTrivia())));

				result[assignment] = BuildPropertyVerifyChain(setMockReceiver, setPropertyName, "Set",
						setArgs, setReceivedMethod, setReceivedArgs)
					.WithTriviaFrom(assignment);
			}
		}

		return result;
	}

	private static bool TryExtractReceivedPropertyAccess(ExpressionSyntax expression,
		SemanticModel semanticModel, ISymbol mockSymbol, CancellationToken cancellationToken,
		out ExpressionSyntax? mockReceiver, out SimpleNameSyntax? propertyName,
		out string? receivedMethod, out ArgumentListSyntax? receivedArgs)
	{
		mockReceiver = null;
		propertyName = null;
		receivedMethod = null;
		receivedArgs = null;

		if (expression is not MemberAccessExpressionSyntax propertyAccess ||
		    propertyAccess.Expression is not InvocationExpressionSyntax receivedInvocation ||
		    receivedInvocation.Expression is not MemberAccessExpressionSyntax receivedAccess)
		{
			return false;
		}

		string method = receivedAccess.Name.Identifier.Text;
		if (method is not ("Received" or "DidNotReceive"))
		{
			return false;
		}

		if (!IsTrackedMockReceiver(receivedAccess.Expression, semanticModel, mockSymbol, cancellationToken))
		{
			return false;
		}

		mockReceiver = receivedAccess.Expression;
		propertyName = propertyAccess.Name;
		receivedMethod = method;
		receivedArgs = receivedInvocation.ArgumentList;
		return true;
	}

	private static InvocationExpressionSyntax BuildPropertyVerifyChain(ExpressionSyntax? mockReceiver,
		SimpleNameSyntax? propertyName, string accessor, ArgumentListSyntax accessorArgs,
		string? receivedMethod, ArgumentListSyntax? receivedArgs)
	{
		MemberAccessExpressionSyntax mockAccess = SyntaxFactory.MemberAccessExpression(
			SyntaxKind.SimpleMemberAccessExpression,
			mockReceiver!,
			SyntaxFactory.IdentifierName("Mock"));
		MemberAccessExpressionSyntax verifyMember = SyntaxFactory.MemberAccessExpression(
			SyntaxKind.SimpleMemberAccessExpression,
			mockAccess,
			SyntaxFactory.IdentifierName("Verify"));
		MemberAccessExpressionSyntax propAccess = SyntaxFactory.MemberAccessExpression(
			SyntaxKind.SimpleMemberAccessExpression,
			verifyMember,
			propertyName!.WithoutTrivia());
		MemberAccessExpressionSyntax accessorAccess = SyntaxFactory.MemberAccessExpression(
			SyntaxKind.SimpleMemberAccessExpression,
			propAccess,
			SyntaxFactory.IdentifierName(accessor));
		InvocationExpressionSyntax accessorCall = SyntaxFactory.InvocationExpression(accessorAccess, accessorArgs);

		bool isNegative = receivedMethod == "DidNotReceive";
		return BuildVerifySuffix(accessorCall, isNegative, receivedArgs ?? SyntaxFactory.ArgumentList());
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
			ArgumentListSyntax transformedArgs = TransformNSubstituteArgReferences(outerInvocation.ArgumentList);
			SimpleNameSyntax methodNameSyntax = outerAccess.Name;

			MemberAccessExpressionSyntax verifyAccess = BuildVerifyAccess(mockReceiver, methodNameSyntax);
			ExpressionSyntax verifyInvocation = SyntaxFactory.InvocationExpression(verifyAccess, transformedArgs);

			if (receivedMethod is "ReceivedWithAnyArgs" or "DidNotReceiveWithAnyArgs")
			{
				verifyInvocation = SyntaxFactory.InvocationExpression(
					SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						verifyInvocation,
						SyntaxFactory.IdentifierName("AnyParameters")),
					SyntaxFactory.ArgumentList());
			}

			bool isNegative = receivedMethod is "DidNotReceive" or "DidNotReceiveWithAnyArgs";
			InvocationExpressionSyntax suffix = BuildVerifySuffix(
				(InvocationExpressionSyntax)verifyInvocation, isNegative, receiverCall.ArgumentList);

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
		// DidNotReceive() → .Never(); Received() → .AtLeastOnce(); Received(n) → .Exactly(n) or .Once() when n is 1.
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
