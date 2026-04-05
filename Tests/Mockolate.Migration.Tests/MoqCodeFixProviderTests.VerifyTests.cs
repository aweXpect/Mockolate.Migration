using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.MoqAnalyzer,
	Mockolate.Migration.Analyzers.MoqCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class MoqCodeFixProviderTests
{
	public sealed class VerifyTests
	{
		[Fact]
		public async Task WithItTransforms_MigratesItCalls()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x, int y); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Verify(m => m.Bar(It.IsRegex("^A"), It.Is<int>(n => n > 0)), Times.Once());
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { bool Bar(string x, int y); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.Bar(It.Matches("^A").AsRegex(), It.Satisfies<int>(n => n > 0)).Once();
					}
				}
				""");

		[Fact]
		public async Task WithNoTimes_MigratesVerifyToAtLeastOnce()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Verify(m => m.Bar(It.IsAny<string>()));
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.Bar(It.IsAny<string>()).AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task WithTimesBetweenExclusive_AdjustsBounds()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Verify(m => m.Bar(It.IsAny<string>()), Times.Between(3, 5, Range.Exclusive));
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.Bar(It.IsAny<string>()).Between(4, 4);
					}
				}
				""");

		[Theory]
		[InlineData("Times.Never", ".Never()")]
		[InlineData("Times.Never()", ".Never()")]
		[InlineData("Times.Once", ".Once()")]
		[InlineData("Times.Once()", ".Once()")]
		[InlineData("Times.AtLeastOnce", ".AtLeastOnce()")]
		[InlineData("Times.AtLeastOnce()", ".AtLeastOnce()")]
		[InlineData("Times.AtLeast(3)", ".AtLeast(3)")]
		[InlineData("Times.AtMostOnce", ".AtMostOnce()")]
		[InlineData("Times.AtMostOnce()", ".AtMostOnce()")]
		[InlineData("Times.AtMost(4)", ".AtMost(4)")]
		[InlineData("Times.Exactly(5)", ".Exactly(5)")]
		[InlineData("Times.Between(3, 5, Range.Inclusive)", ".Between(3, 5)")]
		public async Task WithTimesProperty_MigratesVerify(string moqTimes, string mockolateTimes)
			=> await Verifier.VerifyCodeFixAsync(
				$$"""
				  using Moq;

				  public interface IFoo { bool Bar(string x); }

				  public class Tests
				  {
				  	public void Test()
				  	{
				  		var mock = [|new Mock<IFoo>()|];
				  		mock.Verify(m => m.Bar(It.IsAny<string>()), {{moqTimes}});
				  	}
				  }
				  """,
				$$"""
				  using Moq;
				  using Mockolate;
				  using Mockolate.Verify;

				  public interface IFoo { bool Bar(string x); }

				  public class Tests
				  {
				  	public void Test()
				  	{
				  		var mock = IFoo.CreateMock();
				  		mock.Mock.Verify.Bar(It.IsAny<string>()){{mockolateTimes}};
				  	}
				  }
				  """);

		[Fact]
		public async Task WithUnrecognizedTimesArg_PreservesOriginalVerifyCall()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { void Bar(); }

				public class Tests
				{
					public void Test(Times timesVar)
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Verify(m => m.Bar(), timesVar);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { void Bar(); }

				public class Tests
				{
					public void Test(Times timesVar)
					{
						var mock = IFoo.CreateMock();
						mock.Verify(m => m.Bar(), timesVar);
					}
				}
				""");
	}
}
