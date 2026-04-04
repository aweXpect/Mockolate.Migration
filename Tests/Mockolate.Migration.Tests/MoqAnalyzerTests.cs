using Mockolate.Migration.Analyzers;
using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpAnalyzerVerifier<Mockolate.Migration.Analyzers.MoqAnalyzer>;

namespace Mockolate.Migration.Tests;

public class MoqAnalyzerTests
{
	[Fact]
	public async Task NewMockExplicit_IsFlagged()
		=> await Verifier.VerifyAnalyzerAsync("""
		                                      using Moq;

		                                      public interface IFoo { }

		                                      public class Tests
		                                      {
		                                      	public void Test()
		                                      	{
		                                      		var mock = {|#0:new Mock<IFoo>()|};
		                                      	}
		                                      }
		                                      """,
			Verifier.Diagnostic(Rules.MoqRule)
				.WithLocation(0));

	[Fact]
	public async Task NewMockTargetTyped_IsFlagged()
		=> await Verifier.VerifyAnalyzerAsync("""
		                                      using Moq;

		                                      public interface IFoo { }

		                                      public class Tests
		                                      {
		                                      	public void Test()
		                                      	{
		                                      		Mock<IFoo> mock = {|#0:new()|};
		                                      	}
		                                      }
		                                      """,
			Verifier.Diagnostic(Rules.MoqRule)
				.WithLocation(0));
}
