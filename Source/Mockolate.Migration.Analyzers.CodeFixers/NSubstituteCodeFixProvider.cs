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

		Dictionary<AssignmentExpressionSyntax, InvocationExpressionSyntax> propertyVerifyReplacements =
			FindAndBuildPropertyVerifyReplacements(compilationUnit, semanticModel, mockSymbol, cancellationToken);

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> clearReplacements =
			FindAndBuildClearReceivedCallsReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		Dictionary<AssignmentExpressionSyntax, InvocationExpressionSyntax> raiseReplacements =
			FindAndBuildRaiseReplacements(compilationUnit, semanticModel, mockSymbol, cancellationToken);

		Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax> whenDoReplacements =
			FindAndBuildWhenDoReplacements(allInvocations, semanticModel, mockSymbol, cancellationToken);

		Dictionary<InvocationExpressionSyntax, IMethodSymbol?> andDoesRenames =
			FindAndDoesRenames(allInvocations, semanticModel, mockSymbol, cancellationToken);

		List<SyntaxNode> nodesToReplace = [substituteCall,];
		nodesToReplace.AddRange(setupReplacements.Keys);
		nodesToReplace.AddRange(verifyReplacements.Keys);
		nodesToReplace.AddRange(clearReplacements.Keys);
		nodesToReplace.AddRange(raiseReplacements.Keys);
		nodesToReplace.AddRange(whenDoReplacements.Keys);
		nodesToReplace.AddRange(propertyVerifyReplacements.Keys);
		nodesToReplace.AddRange(andDoesRenames.Keys);

		compilationUnit = compilationUnit.ReplaceNodes(
			nodesToReplace,
			(original, rewritten) =>
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

					if (andDoesRenames.TryGetValue(invocation, out IMethodSymbol? andDoesTarget) &&
					    rewritten is InvocationExpressionSyntax rewrittenInvocation &&
					    rewrittenInvocation.Expression is MemberAccessExpressionSyntax rewrittenAccess)
					{
						// The inner setup rewrite has already been applied to `rewritten`, but its nodes are
						// not bound to the original SyntaxTree — semantic queries must run against the
						// original argument syntax on `invocation`. The resulting (detached) ArgumentSyntax
						// is then attached onto rewrittenInvocation's ArgumentList.
						ArgumentListSyntax andDoesArgs = rewrittenInvocation.ArgumentList;
						SyntaxTriviaList? andDoesTodo = null;
						if (invocation.ArgumentList.Arguments.Count == 1)
						{
							ArgumentSyntax rewrittenArg = RewriteCallInfoCallback(
								invocation.ArgumentList.Arguments[0], andDoesTarget, semanticModel,
								cancellationToken, out CallbackRewriteOutcome outcome);
							if (outcome != CallbackRewriteOutcome.NoChange)
							{
								andDoesArgs = andDoesArgs.WithArguments(SyntaxFactory.SingletonSeparatedList(rewrittenArg));
							}

							if (outcome == CallbackRewriteOutcome.NeedsTodo)
							{
								andDoesTodo = BuildCallInfoTodoTrivia(invocation);
							}
						}

						InvocationExpressionSyntax renamed = rewrittenInvocation
							.WithExpression(rewrittenAccess.WithName(SyntaxFactory.IdentifierName("Do")))
							.WithArgumentList(andDoesArgs);
						return andDoesTodo is { } trivia
							? renamed.WithLeadingTrivia(trivia)
							: renamed;
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

				int? methodParameterCount = GetMethodParameterCount(targetInvocation, semanticModel, cancellationToken);
				ArgumentListSyntax transformedArgs = WrapPlainValuesForLargeArities(
					TransformNSubstituteArgReferences(targetInvocation.ArgumentList, semanticModel, cancellationToken),
					methodParameterCount);

				MemberAccessExpressionSyntax setupAccess = BuildSetupAccess(
					targetMemberAccess.Expression, targetMemberAccess.Name);
				InvocationExpressionSyntax setupInvocation = SyntaxFactory.InvocationExpression(setupAccess, transformedArgs)
					.WithTriviaFrom(targetInvocation);

				IMethodSymbol? receiverMethodSymbol = configuratorMethod is "Returns" or "ReturnsForAnyArgs"
					? semanticModel.GetSymbolInfo(targetInvocation, cancellationToken).Symbol as IMethodSymbol
					: null;
				(InvocationExpressionSyntax effectiveOuter, bool callInfoTodoNeeded) = MaybeRewriteCallInfoArgs(
					outerInvocation, configuratorMethod, receiverMethodSymbol, semanticModel, cancellationToken);

				bool isNested = targetMemberAccess.Expression is MemberAccessExpressionSyntax;
				BuildSequentialOuterIfNeeded(effectiveOuter, configuratorMethod, setupInvocation,
					out InvocationExpressionSyntax? sequentialReplacement);
				InvocationExpressionSyntax? outerReplacement = sequentialReplacement
				                                               ?? (isNested
					                                               ? BuildSimpleOuter(setupInvocation, effectiveOuter, configuratorMethod)
					                                               : null);

				// Lambda args were rewritten OR the CallInfo callback bailed to Case C — either way, force
				// an outer rebuild so the new args / TODO trivia have a node to attach to.
				if (outerReplacement is null && (effectiveOuter != outerInvocation || callInfoTodoNeeded))
				{
					outerReplacement = BuildSimpleOuter(setupInvocation, effectiveOuter, configuratorMethod);
				}

				if (outerReplacement is not null)
				{
					outerReplacement = ApplySetupTrivia(outerReplacement, outerInvocation,
						isNested ? targetMemberAccess.Expression : null, callInfoTodoNeeded);
					result[outerInvocation] = outerReplacement;
				}
				else
				{
					result[targetInvocation] = setupInvocation;
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

				// Property setups have no method-parameter list to bind a CallInfo lambda against, so any
				// `call.X` reference in a Returns lambda will fall through to Case C (TODO). Pure Case A
				// (lambda body never reads `call`) still rewrites cleanly.
				(InvocationExpressionSyntax effectivePropertyOuter, bool propertyCallInfoTodoNeeded) =
					MaybeRewriteCallInfoArgs(outerInvocation, configuratorMethod, null,
						semanticModel, cancellationToken);

				bool isNestedProperty = targetPropertyAccess.Expression is MemberAccessExpressionSyntax;
				BuildSequentialOuterIfNeeded(effectivePropertyOuter, configuratorMethod, setupAccess,
					out InvocationExpressionSyntax? sequentialPropertyReplacement);
				InvocationExpressionSyntax? outerPropertyReplacement = sequentialPropertyReplacement
				                                                       ?? (isNestedProperty
					                                                       ? BuildSimpleOuter(setupAccess, effectivePropertyOuter, configuratorMethod)
					                                                       : null);

				if (outerPropertyReplacement is null &&
				    (effectivePropertyOuter != outerInvocation || propertyCallInfoTodoNeeded))
				{
					outerPropertyReplacement = BuildSimpleOuter(setupAccess, effectivePropertyOuter, configuratorMethod);
				}

				if (outerPropertyReplacement is not null)
				{
					outerPropertyReplacement = ApplySetupTrivia(outerPropertyReplacement, outerInvocation,
						isNestedProperty ? targetPropertyAccess.Expression : null, propertyCallInfoTodoNeeded);
					result[outerInvocation] = outerPropertyReplacement;
				}
				else
				{
					result[targetPropertyAccess] = setupAccess.WithTriviaFrom(targetPropertyAccess);
				}
			}
		}

		return result;
	}

	/// <summary>
	///     Pre-rewrites lambda arguments on the outer configurator (e.g. <c>.Returns(call =&gt; …)</c>) when
	///     the configurator is one that accepts a <c>Func&lt;CallInfo, T&gt;</c> overload. Returns the (possibly
	///     unchanged) <paramref name="outerInvocation" /> together with a flag indicating whether any of the
	///     rewrites bailed to Case C and a TODO comment is required.
	/// </summary>
	private static (InvocationExpressionSyntax effectiveOuter, bool callInfoTodoNeeded) MaybeRewriteCallInfoArgs(
		InvocationExpressionSyntax outerInvocation, string configuratorMethod, IMethodSymbol? receiverMethod,
		SemanticModel semanticModel, CancellationToken cancellationToken)
	{
		if (configuratorMethod is not ("Returns" or "ReturnsForAnyArgs"))
		{
			return (outerInvocation, false);
		}

		ArgumentListSyntax args = outerInvocation.ArgumentList;
		if (args.Arguments.Count == 0)
		{
			return (outerInvocation, false);
		}

		bool changed = false;
		bool needsTodo = false;
		ArgumentSyntax[] rewritten = new ArgumentSyntax[args.Arguments.Count];
		for (int i = 0; i < args.Arguments.Count; i++)
		{
			ArgumentSyntax newArg = RewriteCallInfoCallback(args.Arguments[i], receiverMethod, semanticModel,
				cancellationToken, out CallbackRewriteOutcome outcome);
			rewritten[i] = newArg;
			if (outcome != CallbackRewriteOutcome.NoChange)
			{
				changed = true;
			}

			if (outcome == CallbackRewriteOutcome.NeedsTodo)
			{
				needsTodo = true;
			}
		}

		if (!changed)
		{
			return (outerInvocation, needsTodo);
		}

		ArgumentListSyntax newArgs = args.WithArguments(SyntaxFactory.SeparatedList(rewritten));
		return (outerInvocation.WithArgumentList(newArgs), needsTodo);
	}

	/// <summary>
	///     Combines the nested-mock and CallInfo TODO comments onto a single setup replacement when both apply.
	///     Either flag may be inactive; if neither is active, the replacement is returned untouched.
	/// </summary>
	private static InvocationExpressionSyntax ApplySetupTrivia(InvocationExpressionSyntax replacement,
		InvocationExpressionSyntax outerInvocation, ExpressionSyntax? nestedNavigationRoot, bool callInfoTodoNeeded)
	{
		if (nestedNavigationRoot is null && !callInfoTodoNeeded)
		{
			return replacement;
		}

		SyntaxTriviaList trivia = outerInvocation.GetLeadingTrivia();
		if (nestedNavigationRoot is not null)
		{
			trivia = AppendTodoComment(trivia, outerInvocation,
				$"// TODO: register the nested '{nestedNavigationRoot}' chain explicitly in the mock setup (Mockolate doesn't auto-mock recursively)");
		}

		if (callInfoTodoNeeded)
		{
			trivia = AppendTodoComment(trivia, outerInvocation,
				"// TODO: review CallInfo usage manually — Mockolate's Do/Returns take typed parameters, not CallInfo");
		}

		return replacement.WithLeadingTrivia(trivia);
	}

	/// <summary>
	///     Builds the trivial outer replacement <c>setup.{configurator}(args)</c> with the original argument list
	///     unchanged. Used when an outer replacement is required (e.g. nested-mock TODO injection) but no argument
	///     transformation is needed.
	/// </summary>
	private static InvocationExpressionSyntax BuildSimpleOuter(ExpressionSyntax setupReceiver,
		InvocationExpressionSyntax outerInvocation, string configuratorMethod) =>
		SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					setupReceiver,
					SyntaxFactory.IdentifierName(configuratorMethod)),
				outerInvocation.ArgumentList)
			.WithTriviaFrom(outerInvocation);

	/// <summary>
	///     Leading trivia for a TODO comment alerting the user that a CallInfo-based callback could not be
	///     rewritten automatically — the original lambda is preserved so the user can do it by hand.
	/// </summary>
	private static SyntaxTriviaList BuildCallInfoTodoTrivia(SyntaxNode anchor) =>
		AppendTodoComment(anchor.GetLeadingTrivia(), anchor,
			"// TODO: review CallInfo usage manually — Mockolate's Do/Returns take typed parameters, not CallInfo");

	private static SyntaxTriviaList AppendTodoComment(SyntaxTriviaList existingLeading, SyntaxNode anchor,
		string commentText)
	{
		SyntaxTrivia indent = existingLeading.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
		string endOfLine = DetectLineEnding(anchor.SyntaxTree.GetRoot());
		return existingLeading
			.Add(SyntaxFactory.Comment(commentText))
			.Add(SyntaxFactory.EndOfLine(endOfLine))
			.Add(indent);
	}

	/// <summary>
	///     Builds an outer-level replacement for the configurator chain. Two cases trigger this:
	///     <list type="bullet">
	///         <item>Multi-arg <c>Returns</c>/<c>Throws</c> — split into a chain of single-arg calls.</item>
	///         <item>
	///             <c>ReturnsForAnyArgs</c>/<c>ThrowsForAnyArgs</c> — append <c>.AnyParameters()</c> to the setup
	///             receiver and rename the configurator to its non-<c>ForAnyArgs</c> form (also splitting when
	///             multi-arg).
	///         </item>
	///     </list>
	///     Leaves <paramref name="replacement" /> as <see langword="null" /> when the original single-arg
	///     <c>Returns</c>/<c>Throws</c>/etc. shape can be preserved by the inner-only rewrite.
	/// </summary>
	private static void BuildSequentialOuterIfNeeded(InvocationExpressionSyntax outerInvocation,
		string configuratorMethod, ExpressionSyntax setupReceiver,
		out InvocationExpressionSyntax? replacement)
	{
		replacement = null;

		string? targetMethod;
		bool injectAnyParameters;
		switch (configuratorMethod)
		{
			case "Returns":
				targetMethod = "Returns";
				injectAnyParameters = false;
				break;
			case "Throws":
				targetMethod = "Throws";
				injectAnyParameters = false;
				break;
			case "ReturnsForAnyArgs":
				targetMethod = "Returns";
				injectAnyParameters = true;
				break;
			case "ThrowsForAnyArgs":
				targetMethod = "Throws";
				injectAnyParameters = true;
				break;
			default:
				return;
		}

		// Inner-only rewrite still works for single-arg Returns/Throws with no AnyParameters injection.
		if (!injectAnyParameters && outerInvocation.ArgumentList.Arguments.Count <= 1)
		{
			return;
		}

		ExpressionSyntax current = setupReceiver;
		if (injectAnyParameters)
		{
			current = SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					current,
					SyntaxFactory.IdentifierName("AnyParameters")),
				SyntaxFactory.ArgumentList());
		}

		IReadOnlyList<ArgumentSyntax> outerArgs = outerInvocation.ArgumentList.Arguments;
		if (outerArgs.Count == 0)
		{
			// e.g. ThrowsForAnyArgs<E>() with no value argument — preserve the trailing call shape with no args.
			current = SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					current,
					RenameConfiguratorIdentifier(outerInvocation, targetMethod)),
				SyntaxFactory.ArgumentList());
		}
		else
		{
			foreach (ArgumentSyntax arg in outerArgs)
			{
				current = SyntaxFactory.InvocationExpression(
					SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						current,
						RenameConfiguratorIdentifier(outerInvocation, targetMethod)),
					SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(arg)));
			}
		}

		replacement = ((InvocationExpressionSyntax)current).WithTriviaFrom(outerInvocation);
	}

	/// <summary>
	///     Returns the outer call's identifier renamed to <paramref name="targetName" />, preserving any explicit
	///     type-argument list (so <c>Throws&lt;E&gt;()</c> stays generic when migrating from
	///     <c>ThrowsForAnyArgs&lt;E&gt;()</c>).
	/// </summary>
	private static SimpleNameSyntax RenameConfiguratorIdentifier(InvocationExpressionSyntax outerInvocation, string targetName)
	{
		if (outerInvocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax generic, })
		{
			return SyntaxFactory.GenericName(SyntaxFactory.Identifier(targetName))
				.WithTypeArgumentList(generic.TypeArgumentList);
		}

		return SyntaxFactory.IdentifierName(targetName);
	}

	/// <summary>
	///     Identifies trailing <c>.AndDoes(callback)</c> invocations whose call chain bottoms out at the tracked
	///     mock symbol, mapping each one to the <see cref="IMethodSymbol" /> of the underlying setup target (or
	///     <see langword="null" /> when the bottom of the chain is a property access, in which case CallInfo body
	///     references will fall back to a TODO).
	/// </summary>
	private static Dictionary<InvocationExpressionSyntax, IMethodSymbol?> FindAndDoesRenames(
		IReadOnlyList<InvocationExpressionSyntax> allInvocations,
		SemanticModel? semanticModel,
		ISymbol? mockSymbol,
		CancellationToken cancellationToken)
	{
		Dictionary<InvocationExpressionSyntax, IMethodSymbol?> result = [];
		if (semanticModel is null || mockSymbol is null)
		{
			return result;
		}

		foreach (InvocationExpressionSyntax invocation in allInvocations)
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax access ||
			    access.Name.Identifier.Text != "AndDoes")
			{
				continue;
			}

			if (ChainResolvesToMock(access.Expression, semanticModel, mockSymbol, cancellationToken))
			{
				result[invocation] = FindBottomSetupMethod(access.Expression, semanticModel, cancellationToken);
			}
		}

		return result;
	}

	/// <summary>
	///     Walks a call chain (alternating <see cref="InvocationExpressionSyntax" />/<see cref="MemberAccessExpressionSyntax" />
	///     nodes) and returns <see langword="true" /> when the chain's leftmost receiver is the tracked
	///     <paramref name="mockSymbol" />.
	/// </summary>
	private static bool ChainResolvesToMock(ExpressionSyntax expression, SemanticModel semanticModel,
		ISymbol mockSymbol, CancellationToken cancellationToken)
	{
		ExpressionSyntax current = expression;
		while (true)
		{
			SymbolInfo info = semanticModel.GetSymbolInfo(current, cancellationToken);
			if (SymbolEqualityComparer.Default.Equals(info.Symbol, mockSymbol))
			{
				return true;
			}

			switch (current)
			{
				case InvocationExpressionSyntax inner when inner.Expression is MemberAccessExpressionSyntax innerAccess:
					current = innerAccess.Expression;
					continue;
				case MemberAccessExpressionSyntax memberAccess:
					current = memberAccess.Expression;
					continue;
				default:
					return false;
			}
		}
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

			int? methodParameterCount = GetMethodParameterCount(lambdaBody, semanticModel, cancellationToken);
			ArgumentListSyntax transformedArgs = WrapPlainValuesForLargeArities(
				TransformNSubstituteArgReferences(lambdaBody.ArgumentList, semanticModel, cancellationToken),
				methodParameterCount);
			MemberAccessExpressionSyntax setupAccess = BuildSetupAccess(whenAccess.Expression, lambdaMemberAccess.Name.WithoutTrivia());
			InvocationExpressionSyntax setupCall = SyntaxFactory.InvocationExpression(setupAccess, transformedArgs);

			ArgumentListSyntax doArgs = trailingInvocation.ArgumentList;
			SyntaxTriviaList? callInfoTodo = null;
			if (trailingMethod == "Do" && doArgs.Arguments.Count == 1)
			{
				IMethodSymbol? lambdaTargetMethod =
					semanticModel.GetSymbolInfo(lambdaBody, cancellationToken).Symbol as IMethodSymbol;
				ArgumentSyntax rewrittenDoArg = RewriteCallInfoCallback(doArgs.Arguments[0], lambdaTargetMethod,
					semanticModel, cancellationToken, out CallbackRewriteOutcome outcome);
				if (outcome != CallbackRewriteOutcome.NoChange)
				{
					doArgs = doArgs.WithArguments(SyntaxFactory.SingletonSeparatedList(rewrittenDoArg));
				}

				if (outcome == CallbackRewriteOutcome.NeedsTodo)
				{
					callInfoTodo = BuildCallInfoTodoTrivia(trailingInvocation);
				}
			}

			(string trailingName, ArgumentListSyntax trailingArgs) = trailingMethod == "DoNotCallBase"
				? ("SkippingBaseClass", trailingInvocation.ArgumentList)
				: ("Do", doArgs);

			MemberAccessExpressionSyntax trailingMember = SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				setupCall,
				SyntaxFactory.IdentifierName(trailingName));

			InvocationExpressionSyntax replacement = SyntaxFactory.InvocationExpression(trailingMember, trailingArgs)
				.WithTriviaFrom(trailingInvocation);
			if (callInfoTodo is { } trivia)
			{
				replacement = replacement.WithLeadingTrivia(trivia);
			}

			result[trailingInvocation] = replacement;
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
			    TryExtractReceivedPropertyAccess(assignment.Right, semanticModel, mockSymbol, cancellationToken) is { } got)
			{
				result[assignment] = BuildPropertyVerifyChain(got.MockReceiver, got.PropertyName, "Got",
						SyntaxFactory.ArgumentList(), got.ReceivedMethod, got.ReceivedArgs)
					.WithTriviaFrom(assignment);
				continue;
			}

			// Set pattern: `sub.Received().Prop = value`
			if (TryExtractReceivedPropertyAccess(assignment.Left, semanticModel, mockSymbol, cancellationToken) is { } set)
			{
				ArgumentListSyntax setArgs = BuildPropertyVerifySetArgs(
					assignment.Right, semanticModel, cancellationToken);

				result[assignment] = BuildPropertyVerifyChain(set.MockReceiver, set.PropertyName, "Set",
						setArgs, set.ReceivedMethod, set.ReceivedArgs)
					.WithTriviaFrom(assignment);
			}
		}

		return result;
	}

	private static (ExpressionSyntax MockReceiver, SimpleNameSyntax PropertyName, string ReceivedMethod,
		ArgumentListSyntax ReceivedArgs)? TryExtractReceivedPropertyAccess(ExpressionSyntax expression,
			SemanticModel semanticModel, ISymbol mockSymbol, CancellationToken cancellationToken)
	{
		if (expression is not MemberAccessExpressionSyntax propertyAccess ||
		    propertyAccess.Expression is not InvocationExpressionSyntax receivedInvocation ||
		    receivedInvocation.Expression is not MemberAccessExpressionSyntax receivedAccess)
		{
			return null;
		}

		string method = receivedAccess.Name.Identifier.Text;
		if (method is not ("Received" or "DidNotReceive"))
		{
			return null;
		}

		if (!IsTrackedMockReceiver(receivedAccess.Expression, semanticModel, mockSymbol, cancellationToken))
		{
			return null;
		}

		return (receivedAccess.Expression, propertyAccess.Name, method, receivedInvocation.ArgumentList);
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

	/// <summary>
	///     Builds the argument list for a property setter <c>Verify.Prop.Set(...)</c> call. Translates any
	///     NSubstitute <c>Arg.*</c> matchers in the value to their <c>It.*</c> equivalents and passes the value
	///     through directly — Mockolate's <c>Set(...)</c> takes one parameter and accepts a bare value alongside
	///     the matcher overload, so wrapping a literal in <c>It.Is(...)</c> would just be noise.
	/// </summary>
	private static ArgumentListSyntax BuildPropertyVerifySetArgs(ExpressionSyntax valueExpression,
		SemanticModel semanticModel, CancellationToken cancellationToken)
	{
		// Resolve Arg.* invocations against the original (still-attached) tree so the semantic model
		// can answer GetSymbolInfo, then rewrite them in a detached copy.
		InvocationExpressionSyntax[] argInvocations = valueExpression.DescendantNodesAndSelf()
			.OfType<InvocationExpressionSyntax>()
			.Where(invocation => IsNSubstituteArgCall(invocation, semanticModel, cancellationToken))
			.ToArray();

		ExpressionSyntax transformedValue = argInvocations.Length == 0
			? valueExpression.WithoutTrivia()
			: valueExpression.ReplaceNodes(argInvocations, TransformNSubstituteArgInvocation).WithoutTrivia();

		return SyntaxFactory.ArgumentList(
			SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(transformedValue)));
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
			int? methodParameterCount = GetMethodParameterCount(outerInvocation, semanticModel, cancellationToken);
			ArgumentListSyntax transformedArgs = WrapPlainValuesForLargeArities(
				TransformNSubstituteArgReferences(outerInvocation.ArgumentList, semanticModel, cancellationToken),
				methodParameterCount);
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

	/// <summary>
	///     Mockolate exposes a direct-value overload alongside the matcher overload for any method with up to four
	///     parameters; methods with five or more parameters only expose the matcher overload. When the receiver
	///     method is in the latter group, plain values are wrapped in <c>It.Is(...)</c> so the migrated call still
	///     binds. Existing <c>It.*</c> matchers, predicate lambdas, and ref/out wrappers are preserved untouched.
	/// </summary>
	private static ArgumentListSyntax WrapPlainValuesForLargeArities(ArgumentListSyntax args, int? parameterCount)
	{
		if (parameterCount is null or <= 4)
		{
			return args;
		}

		bool changed = false;
		ArgumentSyntax[] rewritten = new ArgumentSyntax[args.Arguments.Count];
		for (int i = 0; i < args.Arguments.Count; i++)
		{
			ArgumentSyntax arg = args.Arguments[i];
			if (TryWrapAsItIs(arg, out ArgumentSyntax wrapped))
			{
				rewritten[i] = wrapped;
				changed = true;
			}
			else
			{
				rewritten[i] = arg;
			}
		}

		return changed ? args.WithArguments(SyntaxFactory.SeparatedList(rewritten)) : args;
	}

	private static bool TryWrapAsItIs(ArgumentSyntax arg, out ArgumentSyntax wrapped)
	{
		if (!arg.RefKindKeyword.IsKind(SyntaxKind.None))
		{
			wrapped = arg;
			return false;
		}

		if (IsRootedInItInvocation(arg.Expression))
		{
			wrapped = arg;
			return false;
		}

		InvocationExpressionSyntax itIs = SyntaxFactory.InvocationExpression(
			SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				SyntaxFactory.IdentifierName("It"),
				SyntaxFactory.IdentifierName("Is")),
			SyntaxFactory.ArgumentList(
				SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(arg.Expression))));
		wrapped = arg.WithExpression(itIs);
		return true;
	}

	private static bool IsRootedInItInvocation(ExpressionSyntax expression)
	{
		ExpressionSyntax current = expression;
		while (current is InvocationExpressionSyntax invocation)
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			{
				return false;
			}

			if (memberAccess.Expression is IdentifierNameSyntax { Identifier.Text: "It", })
			{
				return true;
			}

			current = memberAccess.Expression;
		}

		return false;
	}

	private static int? GetMethodParameterCount(ExpressionSyntax invocationOrMemberAccess,
		SemanticModel? semanticModel, CancellationToken cancellationToken)
	{
		if (semanticModel is null)
		{
			return null;
		}

		ISymbol? symbol = semanticModel.GetSymbolInfo(invocationOrMemberAccess, cancellationToken).Symbol;
		return symbol is IMethodSymbol methodSymbol ? methodSymbol.Parameters.Length : null;
	}

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
	///     Rewrites a single-arg lambda whose parameter is an NSubstitute <c>CallInfo</c> into a
	///     Mockolate-compatible callback. Three outcomes:
	///     <list type="bullet">
	///         <item>
	///             Body never references the parameter → emit <c>() =&gt; body</c> (Mockolate's
	///             parameterless <c>Do(Action)</c> / <c>Returns(Func&lt;T&gt;)</c> overload).
	///         </item>
	///         <item>
	///             Body uses only statically-resolvable <c>CallInfo</c> accesses (literal-index <c>ArgAt</c>,
	///             literal-index indexer in rvalue position, type-unique <c>Arg&lt;T&gt;()</c>) → rewrite the
	///             lambda parameter list to match the receiver method and replace each access with the matching
	///             parameter name.
	///         </item>
	///         <item>
	///             Anything else (bare <c>call</c>, dynamic indices, indexer-write for out/ref, other CallInfo
	///             APIs) → leave the lambda untouched and return <see cref="CallbackRewriteOutcome.NeedsTodo" />
	///             so the caller emits a TODO comment.
	///         </item>
	///     </list>
	/// </summary>
	private static ArgumentSyntax RewriteCallInfoCallback(ArgumentSyntax argument,
		IMethodSymbol? receiverMethod, SemanticModel semanticModel, CancellationToken cancellationToken,
		out CallbackRewriteOutcome outcome)
	{
		outcome = CallbackRewriteOutcome.NoChange;
		if (argument.Expression is not LambdaExpressionSyntax lambda)
		{
			return argument;
		}

		ParameterSyntax? parameter = lambda switch
		{
			SimpleLambdaExpressionSyntax simple => simple.Parameter,
			ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: { Count: 1, } ps, } => ps[0],
			_ => null,
		};
		if (parameter is null)
		{
			return argument;
		}

		IParameterSymbol? paramSymbol = semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
		if (paramSymbol is null)
		{
			return argument;
		}

		// Symbol-equality match (text-prefiltered for speed) so identifiers that share the
		// parameter's name but resolve to a nested-scope declaration don't count as references.
		bool bodyReferencesParam = lambda.Body.DescendantNodesAndSelf()
			.OfType<IdentifierNameSyntax>()
			.Where(id => id.Identifier.Text == paramSymbol.Name)
			.Any(id => SymbolEqualityComparer.Default.Equals(
				semanticModel.GetSymbolInfo(id, cancellationToken).Symbol, paramSymbol));

		// Case A: body never reads the parameter — drop it entirely so Mockolate's parameterless
		// Action / Func<TResult> overload binds (works on every method arity).
		if (!bodyReferencesParam)
		{
			ParenthesizedLambdaExpressionSyntax dropped = SyntaxFactory.ParenthesizedLambdaExpression(
					SyntaxFactory.ParameterList(),
					lambda.Body)
				.WithTriviaFrom(lambda);
			outcome = CallbackRewriteOutcome.Discarded;
			return argument.WithExpression(dropped);
		}

		// Body references the parameter. If the user already typed it, leave alone.
		bool isCallInfo = paramSymbol.Type is { Name: "CallInfo", } t &&
		                  t.ContainingNamespace?.ToDisplayString() == "NSubstitute.Core";
		if (!isCallInfo)
		{
			return argument;
		}

		// Case B: every reference must be statically resolvable. Any unhandled use → Case C.
		if (receiverMethod is null ||
		    !TryRewriteCallInfoBody(lambda, paramSymbol, receiverMethod, semanticModel,
			    cancellationToken, out LambdaExpressionSyntax? rewritten))
		{
			outcome = CallbackRewriteOutcome.NeedsTodo;
			return argument;
		}

		outcome = CallbackRewriteOutcome.Rewritten;
		return argument.WithExpression(rewritten!);
	}

	private static bool TryRewriteCallInfoBody(LambdaExpressionSyntax lambda, IParameterSymbol paramSymbol,
		IMethodSymbol receiverMethod, SemanticModel semanticModel, CancellationToken cancellationToken,
		out LambdaExpressionSyntax? rewritten)
	{
		rewritten = null;

		// Bail if any local symbol declared inside the body shares a name with one of the receiver
		// method's parameters. Covers locals, foreach variables, catch declarations, pattern variables,
		// local-function parameters, and nested lambda parameters — any of which would either alias the
		// injected parameter (illegal shadowing at the same scope, e.g. `string type = type;`) or
		// re-bind the injected name inside a nested scope so our rewrite would resolve to the wrong thing.
		HashSet<string> paramNames = [.. receiverMethod.Parameters.Select(p => p.Name),];
		foreach (SyntaxNode node in lambda.Body.DescendantNodes())
		{
			ISymbol? declared = semanticModel.GetDeclaredSymbol(node, cancellationToken);
			if (declared is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol &&
			    paramNames.Contains(declared.Name))
			{
				return false;
			}
		}

		Dictionary<SyntaxNode, SyntaxNode> replacements = [];

		foreach (IdentifierNameSyntax reference in lambda.Body.DescendantNodesAndSelf()
			         .OfType<IdentifierNameSyntax>()
			         .Where(id => id.Identifier.Text == paramSymbol.Name))
		{
			// Skip identifiers that share the parameter's name but resolve to a different symbol
			// (e.g. an inner lambda's parameter that shadows the outer CallInfo parameter).
			if (!SymbolEqualityComparer.Default.Equals(
				    semanticModel.GetSymbolInfo(reference, cancellationToken).Symbol, paramSymbol))
			{
				continue;
			}

			switch (reference.Parent)
			{
				// call.ArgAt<T>(N) / call.Arg<T>()
				case MemberAccessExpressionSyntax memberAccess when memberAccess.Expression == reference
				                                                    && memberAccess.Parent is InvocationExpressionSyntax callExpr:
					if (!TryResolveCallInfoCall(callExpr, memberAccess, receiverMethod, semanticModel,
						    cancellationToken, out ExpressionSyntax? callReplacement))
					{
						return false;
					}

					replacements[callExpr] = callReplacement!.WithTriviaFrom(callExpr);
					break;

				// call[N] (rvalue only — write-to-indexer is the out/ref pattern, handled separately).
				case ElementAccessExpressionSyntax elementAccess when elementAccess.Expression == reference:
					if (elementAccess.Parent is AssignmentExpressionSyntax assign && assign.Left == elementAccess)
					{
						return false;
					}

					if (!TryResolveCallInfoIndexer(elementAccess, receiverMethod, out ExpressionSyntax? indexerReplacement))
					{
						return false;
					}

					replacements[elementAccess] = indexerReplacement!.WithTriviaFrom(elementAccess);
					break;

				default:
					// Bare `call`, call.Args(), call.ArgTypes(), call.Target(), call.GetReturnType(), etc.
					return false;
			}
		}

		CSharpSyntaxNode newBody = lambda.Body.ReplaceNodes(replacements.Keys,
			(orig, _) => replacements[orig]);

		SeparatedSyntaxList<ParameterSyntax> newParams = SyntaxFactory.SeparatedList(
			receiverMethod.Parameters.Select(p =>
				SyntaxFactory.Parameter(EscapedIdentifier(p.Name))
					.WithType(SyntaxFactory.ParseTypeName(p.Type.ToDisplayString()))));

		rewritten = SyntaxFactory.ParenthesizedLambdaExpression(
				SyntaxFactory.ParameterList(newParams),
				newBody)
			.WithTriviaFrom(lambda);
		return true;
	}

	private static bool TryResolveCallInfoCall(InvocationExpressionSyntax callExpr,
		MemberAccessExpressionSyntax memberAccess, IMethodSymbol receiverMethod,
		SemanticModel semanticModel, CancellationToken cancellationToken,
		out ExpressionSyntax? replacement)
	{
		replacement = null;
		string method = memberAccess.Name.Identifier.Text;

		if (method == "ArgAt" &&
		    callExpr.ArgumentList.Arguments.Count == 1 &&
		    callExpr.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax { Token.Value: int idx, } &&
		    idx >= 0 && idx < receiverMethod.Parameters.Length)
		{
			replacement = SyntaxFactory.IdentifierName(EscapedIdentifier(receiverMethod.Parameters[idx].Name));
			return true;
		}

		if (method == "Arg" && callExpr.ArgumentList.Arguments.Count == 0 &&
		    memberAccess.Name is GenericNameSyntax { TypeArgumentList.Arguments: { Count: 1, } typeArgs, } &&
		    semanticModel.GetTypeInfo(typeArgs[0], cancellationToken).Type is { } targetType)
		{
			IParameterSymbol[] matches = receiverMethod.Parameters
				.Where(p => SymbolEqualityComparer.Default.Equals(p.Type, targetType))
				.ToArray();
			if (matches.Length != 1)
			{
				return false;
			}

			replacement = SyntaxFactory.IdentifierName(EscapedIdentifier(matches[0].Name));
			return true;
		}

		return false;
	}

	private static bool TryResolveCallInfoIndexer(ElementAccessExpressionSyntax elementAccess,
		IMethodSymbol receiverMethod, out ExpressionSyntax? replacement)
	{
		replacement = null;
		if (elementAccess.ArgumentList.Arguments.Count != 1 ||
		    elementAccess.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax { Token.Value: int idx, } ||
		    idx < 0 || idx >= receiverMethod.Parameters.Length)
		{
			return false;
		}

		replacement = SyntaxFactory.IdentifierName(EscapedIdentifier(receiverMethod.Parameters[idx].Name));
		return true;
	}

	/// <summary>
	///     Builds an identifier token whose source text is escaped with a leading <c>@</c> when
	///     <paramref name="name" /> collides with a reserved C# keyword (e.g. <c>event</c>, <c>class</c>).
	///     Contextual keywords are valid identifiers in expression/parameter positions and are left alone.
	/// </summary>
	private static SyntaxToken EscapedIdentifier(string name) =>
		SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
			? SyntaxFactory.Identifier(default, SyntaxKind.None, "@" + name, name, default)
			: SyntaxFactory.Identifier(name);

	/// <summary>
	///     Walks down a configurator chain (e.g. <c>sub.Bar(1).Returns(v).Throws&lt;E&gt;()</c>) past every
	///     <see cref="SetupConfiguratorMethods" /> entry until it lands on the underlying setup target. Returns
	///     that target's <see cref="IMethodSymbol" />, or <see langword="null" /> when the bottom of the chain is
	///     a property access (no method to map CallInfo accesses against).
	/// </summary>
	private static IMethodSymbol? FindBottomSetupMethod(ExpressionSyntax expression,
		SemanticModel semanticModel, CancellationToken cancellationToken)
	{
		ExpressionSyntax current = expression;
		while (current is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax memberAccess)
		{
			if (SetupConfiguratorMethods.Contains(memberAccess.Name.Identifier.Text) ||
			    memberAccess.Name.Identifier.Text == "AndDoes")
			{
				current = memberAccess.Expression;
				continue;
			}

			return semanticModel.GetSymbolInfo(current, cancellationToken).Symbol as IMethodSymbol;
		}

		return null;
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

	private enum CallbackRewriteOutcome
	{
		/// <summary>Lambda left untouched (already typed by the user, no callback at all, etc.).</summary>
		NoChange,

		/// <summary>Body never referenced the parameter — emitted <c>() =&gt; body</c>.</summary>
		Discarded,

		/// <summary>All <c>call.X</c> references rewritten to typed parameters.</summary>
		Rewritten,

		/// <summary>Could not safely rewrite — original lambda preserved, caller should add a TODO comment.</summary>
		NeedsTodo,
	}
}

#pragma warning restore S3776 // Cognitive Complexity of methods should not be too high
#pragma warning restore S1192
