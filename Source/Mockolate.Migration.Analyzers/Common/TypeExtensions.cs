using Microsoft.CodeAnalysis;

namespace Mockolate.Migration.Analyzers.Common;

internal static class TypeExtensions
{
	private static readonly SymbolDisplayFormat FullyQualifiedNonGenericWithGlobalPrefix = new(
		SymbolDisplayGlobalNamespaceStyle.Included,
		SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
		SymbolDisplayGenericsOptions.None,
		SymbolDisplayMemberOptions.IncludeContainingType,
		SymbolDisplayDelegateStyle.NameAndSignature,
		SymbolDisplayExtensionMethodStyle.Default,
		SymbolDisplayParameterOptions.IncludeType,
		SymbolDisplayPropertyStyle.NameOnly,
		SymbolDisplayLocalOptions.IncludeType
	);

	private static readonly SymbolDisplayFormat? FullyQualifiedGenericWithGlobalPrefix = new(
		SymbolDisplayGlobalNamespaceStyle.Included,
		SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
		SymbolDisplayGenericsOptions.None,
		SymbolDisplayMemberOptions.IncludeContainingType,
		SymbolDisplayDelegateStyle.NameAndSignature,
		SymbolDisplayExtensionMethodStyle.Default,
		SymbolDisplayParameterOptions.IncludeType,
		SymbolDisplayPropertyStyle.NameOnly,
		SymbolDisplayLocalOptions.IncludeType
	);

	public static string GloballyQualified(this ISymbol typeSymbol) =>
		typeSymbol.ToDisplayString(FullyQualifiedGenericWithGlobalPrefix);

	public static string GloballyQualifiedNonGeneric(this ISymbol typeSymbol) =>
		typeSymbol.ToDisplayString(FullyQualifiedNonGenericWithGlobalPrefix);
}
