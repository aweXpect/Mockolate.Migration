using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.MoqAnalyzer,
	Mockolate.Migration.Analyzers.MoqCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class MoqCodeFixProviderTests
{
	public sealed class CallbackTests
	{
		[Fact]
		public async Task WithCallbackMultipleTypeArgs_MigratedToDoWithoutTypeArgs()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x, int y); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.IsAny<string>(), It.IsAny<int>()))
							.Callback<string, int>((x, y) => { })
							.Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar(string x, int y); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.IsAny<string>(), It.IsAny<int>())
							.Do((x, y) => { })
							.Returns(true);
					}
				}
				""");

		[Fact]
		public async Task WithCallbackNoTypeArgs_MigratedToDo()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { void Bar(); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar())
							.Callback(() => { });
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { void Bar(); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar()
							.Do(() => { });
					}
				}
				""");

		[Fact]
		public async Task WithCallbackSingleTypeArg_MigratedToDoWithoutTypeArgs()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.IsAny<string>()))
							.Callback<string>(x => { })
							.Returns(true);
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
						mock.Mock.Setup.Bar(It.IsAny<string>())
							.Do(x => { })
							.Returns(true);
					}
				}
				""");
	}
}
