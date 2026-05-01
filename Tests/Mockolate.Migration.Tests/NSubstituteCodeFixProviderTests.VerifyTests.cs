using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.NSubstituteAnalyzer,
	Mockolate.Migration.Analyzers.NSubstituteCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class NSubstituteCodeFixProviderTests
{
	public sealed class VerifyTests
	{
		[Fact]
		public async Task DidNotReceive_IsRewrittenToNever()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { void Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.DidNotReceive().Bar(1);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { void Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Verify.Bar(1).Never();
					}
				}
				""");

		[Fact]
		public async Task Received_IsRewrittenToAtLeastOnce()
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
						sub.Received().Bar(1);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { void Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Bar(1);
						sub.Mock.Verify.Bar(1).AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task ReceivedExactCount_IsRewrittenToExactly()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { void Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Received(3).Bar(1);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { void Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Verify.Bar(1).Exactly(3);
					}
				}
				""");

		[Fact]
		public async Task ReceivedOne_IsRewrittenToOnce()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { void Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Received(1).Bar(1);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { void Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Verify.Bar(1).Once();
					}
				}
				""");

		[Fact]
		public async Task ReceivedWithArgMatcher_TransformsMatcher()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { void Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Received().Bar(Arg.Any<int>());
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { void Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Verify.Bar(It.IsAny<int>()).AtLeastOnce();
					}
				}
				""");
	}
}
