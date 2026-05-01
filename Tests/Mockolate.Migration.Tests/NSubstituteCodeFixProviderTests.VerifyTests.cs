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
		public async Task DidNotReceivePropertySet_RewritesToVerifySetNever()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.DidNotReceive().Name = "x";
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Verify.Name.Set(It.Is("x")).Never();
					}
				}
				""");

		[Fact]
		public async Task DidNotReceivePropertySetWithArgAny_TranslatesMatcherWithoutWrapping()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.DidNotReceive().Name = Arg.Any<string>();
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Verify.Name.Set(It.IsAny<string>()).Never();
					}
				}
				""");

		[Fact]
		public async Task DidNotReceiveWithAnyArgs_AddsAnyParametersAndNever()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { void Bar(int x, string y); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.DidNotReceiveWithAnyArgs().Bar(default, default);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { void Bar(int x, string y); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Verify.Bar(default, default).AnyParameters().Never();
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
		public async Task ReceivedPropertyGet_RewritesToVerifyGot()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						_ = sub.Received().Name;
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Verify.Name.Got().AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task ReceivedPropertySet_RewritesToVerifySet()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Received().Name = "x";
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Verify.Name.Set(It.Is("x")).AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task ReceivedWithAnyArgs_AddsAnyParameters()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { void Bar(int x, string y); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.ReceivedWithAnyArgs().Bar(default, default);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { void Bar(int x, string y); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Verify.Bar(default, default).AnyParameters().AtLeastOnce();
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
