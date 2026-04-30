using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Mockolate.Migration.Tests.Verifiers;

public static partial class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
	where TAnalyzer : DiagnosticAnalyzer, new()
	where TCodeFix : CodeFixProvider, new()
{
	/// <inheritdoc cref="Microsoft.CodeAnalysis.Diagnostic" />
	public static DiagnosticResult Diagnostic()
		=> CSharpCodeFixVerifier<TAnalyzer, TCodeFix, DefaultVerifier>.Diagnostic();

	/// <inheritdoc cref="Microsoft.CodeAnalysis.Diagnostic" />
	public static DiagnosticResult Diagnostic(string diagnosticId)
		=> CSharpCodeFixVerifier<TAnalyzer, TCodeFix, DefaultVerifier>.Diagnostic(diagnosticId);

	/// <inheritdoc cref="Microsoft.CodeAnalysis.Diagnostic" />
	public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
		=> CSharpCodeFixVerifier<TAnalyzer, TCodeFix, DefaultVerifier>.Diagnostic(descriptor);

	public static async Task VerifyAnalyzerAsync(
		[StringSyntax("c#-test")] string source,
		params DiagnosticResult[] expected
	)
	{
		Test test = new()
		{
			TestCode = source,
			CodeActionValidationMode = CodeActionValidationMode.SemanticStructure,
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80.AddPackages(
			[
				new PackageIdentity("Moq", "4.20.72"),
				new PackageIdentity("NSubstitute", "5.3.0"),
			]),
			TestState =
			{
				AdditionalReferences =
				{
					typeof(MockRegistry).Assembly.Location,
				},
			},
			CompilerDiagnostics = CompilerDiagnostics.None,
		};

		test.ExpectedDiagnostics.AddRange(expected);
		await test.RunAsync(CancellationToken.None);
	}

	/// <inheritdoc cref="CodeFixVerifier{TAnalyzer, TCodeFix, TTest, TVerifier}.VerifyCodeFixAsync(string, string)" />
	public static async Task VerifyCodeFixAsync([StringSyntax("c#-test")] string source,
		[StringSyntax("c#-test")] string fixedSource)
		=> await VerifyCodeFixAsync(source, DiagnosticResult.EmptyDiagnosticResults, fixedSource);

	/// <inheritdoc
	///     cref="CodeFixVerifier{TAnalyzer, TCodeFix, TTest, TVerifier}.VerifyCodeFixAsync(string, DiagnosticResult, string)" />
	public static async Task VerifyCodeFixAsync([StringSyntax("c#-test")] string source, DiagnosticResult expected,
		[StringSyntax("c#-test")] string fixedSource)
		=> await VerifyCodeFixAsync(source, [expected,], fixedSource);

	/// <inheritdoc
	///     cref="CodeFixVerifier{TAnalyzer, TCodeFix, TTest, TVerifier}.VerifyCodeFixAsync(string, DiagnosticResult[], string)" />
	public static async Task VerifyCodeFixAsync(
		[StringSyntax("c#-test")] string source,
		IEnumerable<DiagnosticResult> expected,
		[StringSyntax("c#-test")] string fixedSource
	)
	{
		Test test = new()
		{
			TestCode = source,
			FixedCode = fixedSource,
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80.AddPackages(
			[
				new PackageIdentity("Moq", "4.20.72"),
				new PackageIdentity("NSubstitute", "5.3.0"),
			]),
			TestState =
			{
				AdditionalReferences =
				{
					typeof(MockRegistry).Assembly.Location,
				},
			},
			CompilerDiagnostics = CompilerDiagnostics.None,
		};

		test.ExpectedDiagnostics.AddRange(expected);
		await test.RunAsync(CancellationToken.None);
	}

	public static async Task VerifyLegacyCodeFixAsync([StringSyntax("c#-test")] string source,
		[StringSyntax("c#-test")] string fixedSource)
		=> await VerifyLegacyCodeFixAsync(source, DiagnosticResult.EmptyDiagnosticResults, fixedSource);

	/// <inheritdoc
	///     cref="CodeFixVerifier{TAnalyzer, TCodeFix, TTest, TVerifier}.VerifyCodeFixAsync(string, DiagnosticResult[], string)" />
	public static async Task VerifyLegacyCodeFixAsync(
		[StringSyntax("c#-test")] string source,
		IEnumerable<DiagnosticResult> expected,
		[StringSyntax("c#-test")] string fixedSource
	)
	{
		Test test = new()
		{
			TestCode = source,
			FixedCode = fixedSource,
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80.AddPackages(
			[
				new PackageIdentity("Moq", "4.20.72"),
				new PackageIdentity("NSubstitute", "5.3.0"),
			]),
			TestState =
			{
				AdditionalReferences =
				{
					typeof(MockRegistry).Assembly.Location,
				},
			},
			CompilerDiagnostics = CompilerDiagnostics.None,
		};

		test.ExpectedDiagnostics.AddRange(expected);
		await test.RunAsync(CancellationToken.None);
	}
}
