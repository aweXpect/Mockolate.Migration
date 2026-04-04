using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Mockolate.Migration.Analyzers.Common;

namespace Mockolate.Migration.Analyzers;

/// <summary>
///     An analyzer that flags mock usage from Moq.
/// </summary>
/// <remarks>
///     <see href="https://github.com/devlooped/moq" />
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MoqAnalyzer : DiagnosticAnalyzer
{
	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rules.MoqRule,];

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterOperationAction(AnalyzeOperation, OperationKind.Invocation);
		context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
	}

	private static void AnalyzeOperation(OperationAnalysisContext context)
	{
		if (context.Operation is IInvocationOperation invocationOperation)
		{
			IMethodSymbol? methodSymbol = invocationOperation.TargetMethod;

			string? fullyQualifiedNonGenericMethodName = methodSymbol.GloballyQualifiedNonGeneric();

			if (fullyQualifiedNonGenericMethodName.StartsWith("global::Moq") &&
			    fullyQualifiedNonGenericMethodName.EndsWith("Mock"))
			{
				SyntaxNode syntax = invocationOperation.Syntax;
				while (syntax.Parent is ExpressionOrPatternSyntax && syntax.Parent is not AwaitExpressionSyntax)
				{
					syntax = syntax.Parent;
				}

				// Do not report nested `.Should()` e.g. in `.Should().AllSatisfy(x => x.Should().BeGreaterThan(0));`
				if (syntax.Parent is not ArgumentSyntax)
				{
					context.ReportDiagnostic(
						Diagnostic.Create(Rules.MoqRule, syntax.GetLocation())
					);
				}
			}
		}
	}

	private static void AnalyzeObjectCreation(OperationAnalysisContext context)
	{
		if (context.Operation is IObjectCreationOperation objectCreationOperation)
		{
			INamedTypeSymbol? typeSymbol = objectCreationOperation.Constructor?.ContainingType;
			if (typeSymbol != null && typeSymbol.ContainingNamespace.ToDisplayString() == "Moq" && typeSymbol.Name == "Mock")
			{
				context.ReportDiagnostic(
					Diagnostic.Create(Rules.MoqRule, objectCreationOperation.Syntax.GetLocation())
				);
			}
		}
	}
}
