using Microsoft.CodeAnalysis;

namespace Mockolate.Migration.Analyzers;

/// <summary>
/// The rules for the analyzers in this project.
/// </summary>
public static class Rules
{
	private const string UsageCategory = "Usage";

	/// <summary>
	/// Migration rule for Moq usage. Flags any usage of `new Mock&lt;T&gt;()` or `new Mock&lt;T&gt;()` with target-typed new.
	/// </summary>
	public static readonly DiagnosticDescriptor MoqRule =
		CreateDescriptor("MockolateM001", UsageCategory, DiagnosticSeverity.Warning);


	private static DiagnosticDescriptor CreateDescriptor(string diagnosticId, string category,
		DiagnosticSeverity severity) => new(
		diagnosticId,
		new LocalizableResourceString(diagnosticId + "Title",
			Resources.ResourceManager, typeof(Resources)),
		new LocalizableResourceString(diagnosticId + "MessageFormat", Resources.ResourceManager,
			typeof(Resources)),
		category,
		severity,
		true,
		new LocalizableResourceString(diagnosticId + "Description", Resources.ResourceManager,
			typeof(Resources))
	);
}
