using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.NSubstituteAnalyzer,
	Mockolate.Migration.Analyzers.NSubstituteCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class NSubstituteCodeFixProviderTests
{
	public sealed class NestedTests
	{
		[Fact]
		public async Task NestedMethod_RewritesAndAddsTodo()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IBar { int Compute(int x); }
				public interface IFoo { IBar Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Child.Compute(1).Returns(42);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IBar { int Compute(int x); }
				public interface IFoo { IBar Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						// TODO: register the nested 'sub.Child' chain explicitly in the mock setup (Mockolate doesn't auto-mock recursively)
						sub.Child.Mock.Setup.Compute(1).Returns(42);
					}
				}
				""");

		[Fact]
		public async Task NestedProperty_RewritesAndAddsTodo()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IBar { string Name { get; } }
				public interface IFoo { IBar Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Child.Name.Returns("baz");
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IBar { string Name { get; } }
				public interface IFoo { IBar Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						// TODO: register the nested 'sub.Child' chain explicitly in the mock setup (Mockolate doesn't auto-mock recursively)
						sub.Child.Mock.Setup.Name.Returns("baz");
					}
				}
				""");
	}
}
