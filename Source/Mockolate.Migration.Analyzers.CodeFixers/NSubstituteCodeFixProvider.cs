using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mockolate.Migration.Analyzers;

/// <summary>
///     A code fix provider that migrates NSubstitute to Mockolate.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NSubstituteCodeFixProvider))]
[Shared]
public class NSubstituteCodeFixProvider() : AssertionCodeFixProvider(Rules.NSubstituteRule)
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

		InvocationExpressionSyntax? substituteCall = FindSubstituteCreationCall(expressionSyntax, semanticModel, cancellationToken);
		if (substituteCall is null)
		{
			return document;
		}

		ExpressionSyntax? replacement = BuildCreationReplacement(substituteCall);
		if (replacement is null)
		{
			return document;
		}

		compilationUnit = compilationUnit.ReplaceNode(substituteCall, replacement.WithTriviaFrom(substituteCall));

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
