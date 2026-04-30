using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.NSubstituteAnalyzer,
	Mockolate.Migration.Analyzers.NSubstituteCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class NSubstituteCodeFixProviderTests
{
	public sealed class ClearReceivedCallsTests
	{
		[Fact]
		public async Task ClearReceivedCalls_IsRewrittenToClearAllInteractions()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { void Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(1);
						sub.ClearReceivedCalls();
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { void Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Bar(1);
						sub.Mock.ClearAllInteractions();
					}
				}
				""");
	}
}
