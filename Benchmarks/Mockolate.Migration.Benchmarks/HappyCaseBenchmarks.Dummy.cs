using BenchmarkDotNet.Attributes;

namespace Mockolate.Migration.Benchmarks;

/// <summary>
///     This is a dummy benchmark in the T6e template.
/// </summary>
public partial class HappyCaseBenchmarks
{
	[Benchmark]
	public TimeSpan Dummy_aweXpect()
		=> TimeSpan.FromSeconds(10);
}
