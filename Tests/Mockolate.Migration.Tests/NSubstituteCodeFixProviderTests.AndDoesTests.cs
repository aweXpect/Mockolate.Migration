using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.NSubstituteAnalyzer,
	Mockolate.Migration.Analyzers.NSubstituteCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class NSubstituteCodeFixProviderTests
{
	public sealed class AndDoesTests
	{
		[Fact]
		public async Task AndDoes_AfterMultiArgReturns_RewritesToDoOnFinalCall()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						int counter = 0;
						sub.Bar(1).Returns(1, 2, 3).AndDoes(call => counter++);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						int counter = 0;
						sub.Mock.Setup.Bar(1).Returns(1).Returns(2).Returns(3).Do(() => counter++);
					}
				}
				""");

		[Fact]
		public async Task AndDoes_AfterReturns_RewritesToDo()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						int counter = 0;
						sub.Bar(1).Returns(42).AndDoes(call => counter++);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						int counter = 0;
						sub.Mock.Setup.Bar(1).Returns(42).Do(() => counter++);
					}
				}
				""");
	}
}
