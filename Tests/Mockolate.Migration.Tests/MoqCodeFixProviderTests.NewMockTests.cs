using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.MoqAnalyzer,
	Mockolate.Migration.Analyzers.MoqCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class MoqCodeFixProviderTests
{
	public sealed class NewMockTests
	{
		[Fact]
		public async Task IsReplaced()
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
		public async Task TargetTyped_IsReplaced()
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

		[Fact]
		public async Task WithExistingMockolateUsing_DoesNotDuplicateUsing()
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
		public async Task WithGenericSetupCall_PreservesTypeArguments()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar<T>(T x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar<int>(It.IsAny<int>())).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar<T>(T x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar<int>(It.IsAny<int>()).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task WithNestedSetupCall_SetupIsNotRewritten()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IBar { bool Bar(string x); }
				public interface IChild { IBar GrandChild { get; } }
				public interface IFoo { IChild Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Child.GrandChild.Bar(It.IsAny<string>())).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IBar { bool Bar(string x); }
				public interface IChild { IBar GrandChild { get; } }
				public interface IFoo { IChild Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Child.GrandChild.Mock.Bar(It.IsAny<string>()).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task WithObjectAccess_RemovesObjectProperty()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						var obj = mock.Object;
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
						var obj = mock;
					}
				}
				""");

		[Fact]
		public async Task WithSetupCall_MigratesSetup()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.IsAny<string>())).Returns(true);
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
						mock.Mock.Setup.Bar(It.IsAny<string>()).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task WithSetupCallMultipleArgs_MigratesSetup()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x, int y); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
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
						mock.Mock.Setup.Bar(It.IsAny<string>(), It.IsAny<int>()).Returns(true);
					}
				}
				""");
	}
}
