using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.MoqAnalyzer,
	Mockolate.Migration.Analyzers.MoqCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class MoqCodeFixProviderTests
{
	public sealed class PropertyTests
	{
		[Fact]
		public async Task SetupGet_MigratesToSetupPropertyAccess()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.SetupGet(m => m.Name).Returns("bar");
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
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Name.Returns("bar");
					}
				}
				""");

		[Fact]
		public async Task VerifyGet_WithoutTimes_MigratesToGotAtLeastOnce()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.VerifyGet(m => m.Name);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.Name.Got().AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task VerifyGet_WithTimesOnce_MigratesToGotOnce()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.VerifyGet(m => m.Name, Times.Once());
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.Name.Got().Once();
					}
				}
				""");

		[Fact]
		public async Task VerifySet_WithLiteral_WrapsInItIs()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.VerifySet(m => m.Name = "foo");
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.Name.Set(It.Is("foo")).AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task VerifySet_WithTimesNever_MigratesToSetNever()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.VerifySet(m => m.Name = "foo", Times.Never);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.Name.Set(It.Is("foo")).Never();
					}
				}
				""");

		[Fact]
		public async Task VerifySet_WithItIsAnyMatcher_PreservesMatcher()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { int Value { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.VerifySet(m => m.Value = It.IsAny<int>(), Times.Once());
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { int Value { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.Value.Set(It.IsAny<int>()).Once();
					}
				}
				""");

		[Fact]
		public async Task VerifySet_WithItIsInRange_TransformsMatcher()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { int Value { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.VerifySet(m => m.Value = It.IsInRange(1, 5, Range.Inclusive));
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { int Value { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.Value.Set(It.IsInRange(1, 5)).AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task VerifySet_WithItIsInRangeExclusive_PreservesChainedMatcher()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { int Value { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.VerifySet(m => m.Value = It.IsInRange(1, 5, Range.Exclusive));
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { int Value { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.Value.Set(It.IsInRange(1, 5).Exclusive()).AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task VerifySet_WithItIsRegex_PreservesChainedMatcher()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.VerifySet(m => m.Name = It.IsRegex("^foo$"), Times.Once());
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.Name.Set(It.Matches("^foo$").AsRegex()).Once();
					}
				}
				""");

		[Fact]
		public async Task VerifyGet_WithNestedProperty_UsesNavigationChain()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IBar { string Name { get; set; } }
				public interface IFoo { IBar Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.VerifyGet(m => m.Child.Name, Times.Exactly(2));
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public interface IBar { string Name { get; set; } }
				public interface IFoo { IBar Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Child.Mock.Verify.Name.Got().Exactly(2);
					}
				}
				""");
	}
}
