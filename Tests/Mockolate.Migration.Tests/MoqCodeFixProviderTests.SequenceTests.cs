using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.MoqAnalyzer,
	Mockolate.Migration.Analyzers.MoqCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class MoqCodeFixProviderTests
{
	public sealed class SequenceTests
	{
		[Fact]
		public async Task InSequence_OnPropertySetup_StripsInSequenceWrapper()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sequence = new MockSequence();
						var mock = [|new Mock<IFoo>()|];
						mock.InSequence(sequence).Setup(m => m.Name).Returns("first");
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sequence = new MockSequence();
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Name.Returns("first");
					}
				}
				""");

		[Fact]
		public async Task InSequence_OnSetup_StripsInSequenceWrapper()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var sequence = new MockSequence();
						var mock = [|new Mock<IFoo>()|];
						mock.InSequence(sequence).Setup(m => m.Bar("a")).Returns(true);
						mock.InSequence(sequence).Setup(m => m.Bar("b")).Returns(false);
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
						var sequence = new MockSequence();
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar("a").Returns(true);
						mock.Mock.Setup.Bar("b").Returns(false);
					}
				}
				""");

		[Fact]
		public async Task InSequence_OnSetupProperty_StripsInSequenceWrapper()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sequence = new MockSequence();
						var mock = [|new Mock<IFoo>()|];
						mock.InSequence(sequence).SetupProperty(f => f.Name, "foo");
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var sequence = new MockSequence();
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Name.InitializeWith("foo");
					}
				}
				""");

		[Fact]
		public async Task InSequence_OnSetupSequence_StripsInSequenceWrapper()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { int GetCount(); }

				public class Tests
				{
					public void Test()
					{
						var sequence = new MockSequence();
						var mock = [|new Mock<IFoo>()|];
						mock.InSequence(sequence).SetupSequence(f => f.GetCount()).Returns(1).Returns(2);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { int GetCount(); }

				public class Tests
				{
					public void Test()
					{
						var sequence = new MockSequence();
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.GetCount().Returns(1).Returns(2);
					}
				}
				""");

		[Fact]
		public async Task InSequence_OnSetupWithReturnsAsync_StripsInSequenceWrapper()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;
				using System.Threading.Tasks;

				public interface IFoo { Task<int> GetAsync(); Task<string> GetNameAsync(); }

				public class Tests
				{
					public void Test()
					{
						var sequence = new MockSequence();
						var mock = [|new Mock<IFoo>()|];
						mock.InSequence(sequence).Setup(m => m.GetAsync()).ReturnsAsync(1);
						mock.InSequence(sequence).Setup(m => m.GetNameAsync()).ReturnsAsync("ok");
					}
				}
				""",
				"""
				using Moq;
				using System.Threading.Tasks;
				using Mockolate;

				public interface IFoo { Task<int> GetAsync(); Task<string> GetNameAsync(); }

				public class Tests
				{
					public void Test()
					{
						var sequence = new MockSequence();
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.GetAsync().ReturnsAsync(1);
						mock.Mock.Setup.GetNameAsync().ReturnsAsync("ok");
					}
				}
				""");

		[Fact]
		public async Task InSequence_WithItMatcher_MigratesMatcher()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var sequence = new MockSequence();
						var mock = [|new Mock<IFoo>()|];
						mock.InSequence(sequence).Setup(m => m.Bar(It.IsAny<string>())).Returns(true);
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
						var sequence = new MockSequence();
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.IsAny<string>()).Returns(true);
					}
				}
				""");
	}
}
