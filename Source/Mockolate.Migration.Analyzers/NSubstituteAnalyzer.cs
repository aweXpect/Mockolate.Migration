using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Mockolate.Migration.Analyzers;

/// <summary>
///     An analyzer that flags substitute creation from NSubstitute (<c>Substitute.For&lt;T&gt;()</c>,
///     <c>Substitute.ForPartsOf&lt;T&gt;()</c> and <c>Substitute.ForTypeForwardingTo&lt;TInterface, TClass&gt;()</c>).
/// </summary>
/// <remarks>
///     <see href="https://nsubstitute.github.io" />
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NSubstituteAnalyzer : DiagnosticAnalyzer
{
	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rules.NSubstituteRule,];

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterOperationAction(AnalyzeOperation, OperationKind.Invocation);
	}

	private static void AnalyzeOperation(OperationAnalysisContext context)
	{
		if (context.Operation is not IInvocationOperation invocationOperation)
		{
			return;
		}

		IMethodSymbol methodSymbol = invocationOperation.TargetMethod;
		INamedTypeSymbol? containingType = methodSymbol.ContainingType;

		if (containingType is null ||
		    containingType.Name != "Substitute" ||
		    containingType.ContainingNamespace?.ToDisplayString() != "NSubstitute")
		{
			return;
		}

		if (methodSymbol.Name is not ("For" or "ForPartsOf" or "ForTypeForwardingTo"))
		{
			return;
		}

		SyntaxNode syntax = invocationOperation.Syntax;
		while (syntax.Parent is ExpressionOrPatternSyntax && syntax.Parent is not AwaitExpressionSyntax)
		{
			syntax = syntax.Parent;
		}

		if (syntax.Parent is ArgumentSyntax)
		{
			return;
		}

		context.ReportDiagnostic(
			Diagnostic.Create(Rules.NSubstituteRule, syntax.GetLocation())
		);
	}
}
