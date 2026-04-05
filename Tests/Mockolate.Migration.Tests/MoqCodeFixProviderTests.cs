using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.MoqAnalyzer,
	Mockolate.Migration.Analyzers.MoqCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public class MoqCodeFixProviderTests
{
	[Fact]
	public async Task NewMockExplicit_IsReplaced()
		=> await Verifier.VerifyCodeFixAsync(
			"""
			using Moq;

			public interface IFoo { }

			public class Tests
			{
				public void Test()
				{
					var mock = [|new Mock<IFoo>()|];
				}
			}
			""",
			"""
			using Moq;
			using Mockolate;

			public interface IFoo { }

			public class Tests
			{
				public void Test()
				{
					var mock = IFoo.CreateMock();
				}
			}
			""");

	[Fact]
	public async Task NewMockExplicit_WithExistingMockolateUsing_DoesNotDuplicateUsing()
		=> await Verifier.VerifyCodeFixAsync(
			"""
			using Moq;
			using Mockolate;

			public interface IFoo { }

			public class Tests
			{
				public void Test()
				{
					var mock = [|new Mock<IFoo>()|];
				}
			}
			""",
			"""
			using Moq;
			using Mockolate;

			public interface IFoo { }

			public class Tests
			{
				public void Test()
				{
					var mock = IFoo.CreateMock();
				}
			}
			""");

	[Fact]
	public async Task NewMockTargetTyped_IsReplaced()
		=> await Verifier.VerifyCodeFixAsync(
			"""
			using Moq;

			public interface IFoo { }

			public class Tests
			{
				public void Test()
				{
					Mock<IFoo> mock = [|new()|];
				}
			}
			""",
			"""
			using Moq;
			using Mockolate;

			public interface IFoo { }

			public class Tests
			{
				public void Test()
				{
					IFoo mock = IFoo.CreateMock();
				}
			}
			""");
}
