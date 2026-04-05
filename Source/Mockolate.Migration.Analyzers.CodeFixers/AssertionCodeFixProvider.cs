using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mockolate.Migration.Analyzers;

/// <summary>
///     Base class for code fix provider that migrates assertions to aweXpect.
/// </summary>
public abstract class AssertionCodeFixProvider(DiagnosticDescriptor rule) : CodeFixProvider
{
	/// <inheritdoc />
	public sealed override ImmutableArray<string> FixableDiagnosticIds { get; } = [rule.Id,];

	/// <inheritdoc />
	public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	/// <inheritdoc />
	public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		foreach (Diagnostic? diagnostic in context.Diagnostics)
		{
			TextSpan diagnosticSpan = diagnostic.Location.SourceSpan;

			SyntaxNode? root =
				await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

			if (root?.FindNode(diagnosticSpan) is ExpressionSyntax expressionSyntax
			    and (InvocationExpressionSyntax or ConditionalAccessExpressionSyntax or LambdaExpressionSyntax
			        or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
			{
				context.RegisterCodeFix(
					CodeAction.Create(
						rule.Title.ToString(),
						c => ConvertAssertionAsync(context, expressionSyntax, c),
						rule.Title.ToString()),
					diagnostic);
			}
		}
	}

	/// <summary>
	///     Converts the assertion.
	/// </summary>
	protected abstract Task<Document> ConvertAssertionAsync(CodeFixContext context,
		ExpressionSyntax expressionSyntax, CancellationToken cancellationToken);
}
