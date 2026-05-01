using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.NSubstituteAnalyzer,
	Mockolate.Migration.Analyzers.NSubstituteCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class NSubstituteCodeFixProviderTests
{
	public sealed class SetupTests
	{
		[Fact]
		public async Task ArgAny_IsRewrittenToItIsAny()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(Arg.Any<int>()).Returns(42);
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
						sub.Mock.Setup.Bar(It.IsAny<int>()).Returns(42);
					}
				}
				""");

		[Fact]
		public async Task ArgCompat_IsRewrittenToItMatchers()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { int Sum(int x, int y); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Sum(Arg.Compat.Any<int>(), Arg.Compat.Is<int>(y => y > 0)).Returns(42);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { int Sum(int x, int y); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Setup.Sum(It.IsAny<int>(), It.Satisfies<int>(y => y > 0)).Returns(42);
					}
				}
				""");

		[Fact]
		public async Task ArgIsPredicate_IsRewrittenToItSatisfies()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(Arg.Is<int>(x => x > 0)).Returns(42);
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
						sub.Mock.Setup.Bar(It.Satisfies<int>(x => x > 0)).Returns(42);
					}
				}
				""");

		[Fact]
		public async Task ArgIsValue_IsRewrittenToItIs()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(Arg.Is(5)).Returns(42);
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
						sub.Mock.Setup.Bar(It.Is(5)).Returns(42);
					}
				}
				""");

		[Fact]
		public async Task MethodReturns_IsRewrittenToMockSetup()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(1).Returns(42);
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
						sub.Mock.Setup.Bar(1).Returns(42);
					}
				}
				""");

		[Fact]
		public async Task MethodThrowsGeneric_IsRewrittenToMockSetup()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using System;
				using NSubstitute;
				using NSubstitute.ExceptionExtensions;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(1).Throws<InvalidOperationException>();
					}
				}
				""",
				"""
				using System;
				using NSubstitute;
				using NSubstitute.ExceptionExtensions;
				using Mockolate;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Setup.Bar(1).Throws<InvalidOperationException>();
					}
				}
				""");

		[Fact]
		public async Task MultipleMatchers_AreAllRewritten()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { int Sum(int x, int y); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Sum(Arg.Any<int>(), Arg.Is<int>(y => y > 0)).Returns(42);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { int Sum(int x, int y); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Setup.Sum(It.IsAny<int>(), It.Satisfies<int>(y => y > 0)).Returns(42);
					}
				}
				""");

		[Fact]
		public async Task PropertyReturns_IsRewrittenToMockSetup()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { string Name { get; } }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Name.Returns("bar");
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { string Name { get; } }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Setup.Name.Returns("bar");
					}
				}
				""");

		[Fact]
		public async Task ReturnsForAnyArgs_AddsAnyParametersAndRenamesToReturns()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { int Bar(int x, string y); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(default, default).ReturnsForAnyArgs(42);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { int Bar(int x, string y); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Setup.Bar(default, default).AnyParameters().Returns(42);
					}
				}
				""");

		[Fact]
		public async Task ReturnsForAnyArgsSequential_SplitsAndKeepsAnyParameters()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(default).ReturnsForAnyArgs(1, 2, 3);
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
						sub.Mock.Setup.Bar(default).AnyParameters().Returns(1).Returns(2).Returns(3);
					}
				}
				""");

		[Fact]
		public async Task SequentialPropertyReturns_AreSplitIntoChain()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { string Name { get; } }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Name.Returns("a", "b", "c");
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { string Name { get; } }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Setup.Name.Returns("a").Returns("b").Returns("c");
					}
				}
				""");

		[Fact]
		public async Task SequentialReturns_AreSplitIntoChain()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(1).Returns(1, 2, 3);
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
						sub.Mock.Setup.Bar(1).Returns(1).Returns(2).Returns(3);
					}
				}
				""");

		[Fact]
		public async Task ThrowsForAnyArgsGeneric_AddsAnyParametersAndRenamesToThrows()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using System;
				using NSubstitute;
				using NSubstitute.ExceptionExtensions;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(default).ThrowsForAnyArgs<InvalidOperationException>();
					}
				}
				""",
				"""
				using System;
				using NSubstitute;
				using NSubstitute.ExceptionExtensions;
				using Mockolate;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Setup.Bar(default).AnyParameters().Throws<InvalidOperationException>();
					}
				}
				""");

		[Fact]
		public async Task ThrowsForAnyArgsSequential_SplitsAndKeepsAnyParameters()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using System;
				using NSubstitute;
				using NSubstitute.ExceptionExtensions;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(default).ThrowsForAnyArgs(new InvalidOperationException(), new ArgumentException());
					}
				}
				""",
				"""
				using System;
				using NSubstitute;
				using NSubstitute.ExceptionExtensions;
				using Mockolate;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Setup.Bar(default).AnyParameters().Throws(new InvalidOperationException()).Throws(new ArgumentException());
					}
				}
				""");

		[Fact]
		public async Task ThrowsForAnyArgsWithException_AddsAnyParametersAndRenamesToThrows()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using System;
				using NSubstitute;
				using NSubstitute.ExceptionExtensions;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.Bar(default).ThrowsForAnyArgs(new InvalidOperationException());
					}
				}
				""",
				"""
				using System;
				using NSubstitute;
				using NSubstitute.ExceptionExtensions;
				using Mockolate;

				public interface IFoo { int Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Setup.Bar(default).AnyParameters().Throws(new InvalidOperationException());
					}
				}
				""");
	}
}
