using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.MoqAnalyzer,
	Mockolate.Migration.Analyzers.MoqCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class MoqCodeFixProviderTests
{
	public sealed class SetupTests
	{
		[Fact]
		public async Task WithItIsIn_MigratedToIsOneOf()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.IsIn<string>(new[] { "A", "B" }))).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.IsOneOf<string>(new[] { "A", "B" })).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task WithItIsInRangeExclusive_ChainsExclusive()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.IsInRange<int>(1, 10, Range.Exclusive))).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.IsInRange<int>(1, 10).Exclusive()).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task WithItIsInRangeInclusive_DropsRangeArg()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(Moq.It.IsInRange<int>(1, 10, Range.Inclusive))).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.IsInRange<int>(1, 10)).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task WithItIsLambda_MigratedToSatisfies()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.Is<string>(s => s.StartsWith("A")))).Returns(true);
						mock.Setup(m => m.Bar(It.Is<string>((x) => x.StartsWith("B"))))
							.Returns(false);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.Satisfies<string>(s => s.StartsWith("A"))).Returns(true);
						mock.Mock.Setup.Bar(It.Satisfies<string>((x) => x.StartsWith("B")))
							.Returns(false);
					}
				}
				""");

		[Fact]
		public async Task WithItIsRegex_MigratedToMatchesAsRegex()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.IsRegex("^A"))).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.Matches("^A").AsRegex()).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task WithItIsTwoArgs_MigratedToIs()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;
				using System;
				using System.Collections.Generic;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.Is<string>("hello", StringComparer.OrdinalIgnoreCase))).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using System;
				using System.Collections.Generic;
				using Mockolate;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.Is<string>("hello").Using(StringComparer.OrdinalIgnoreCase)).Returns(true);
					}
				}
				""");
	}
}
