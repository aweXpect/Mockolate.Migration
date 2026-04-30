using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.NSubstituteAnalyzer,
	Mockolate.Migration.Analyzers.NSubstituteCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class NSubstituteCodeFixProviderTests
{
	public sealed class CreationTests
	{
		[Fact]
		public async Task SubstituteFor_FullyQualified_IsReplaced()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				public interface IFoo { }

				public class Tests
				{
					public void Test()
					{
						var sub = [|NSubstitute.Substitute.For<IFoo>()|];
					}
				}
				""",
				"""
				using Mockolate;

				public interface IFoo { }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
					}
				}
				""");

		[Fact]
		public async Task SubstituteFor_GlobalQualified_IsReplaced()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				public interface IFoo { }

				public class Tests
				{
					public void Test()
					{
						var sub = [|global::NSubstitute.Substitute.For<IFoo>()|];
					}
				}
				""",
				"""
				using Mockolate;

				public interface IFoo { }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
					}
				}
				""");

		[Fact]
		public async Task SubstituteFor_IsReplaced()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
					}
				}
				""");

		[Fact]
		public async Task SubstituteFor_WithConstructorArguments_PreservesArguments()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public class Foo
				{
					public Foo(string name, int count) { }
				}

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<Foo>("name", 42)|];
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public class Foo
				{
					public Foo(string name, int count) { }
				}

				public class Tests
				{
					public void Test()
					{
						var sub = Foo.CreateMock("name", 42);
					}
				}
				""");

		[Fact]
		public async Task SubstituteFor_WithMultipleInterfaces_ChainsImplementing()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { }
				public interface IBar { }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo, IBar>()|];
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { }
				public interface IBar { }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock().Implementing<IBar>();
					}
				}
				""");

		[Fact]
		public async Task SubstituteFor_WithThreeInterfaces_ChainsImplementingTwice()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { }
				public interface IBar { }
				public interface IBaz { }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo, IBar, IBaz>()|];
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { }
				public interface IBar { }
				public interface IBaz { }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock().Implementing<IBar>().Implementing<IBaz>();
					}
				}
				""");

		[Fact]
		public async Task SubstituteForPartsOf_IsReplacedWithCreateMock()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public class Foo { public virtual int Bar() => 0; }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.ForPartsOf<Foo>()|];
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public class Foo { public virtual int Bar() => 0; }

				public class Tests
				{
					public void Test()
					{
						var sub = Foo.CreateMock();
					}
				}
				""");

		[Fact]
		public async Task SubstituteForTypeForwardingTo_WrapsNewInstance()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { void Bar(); }
				public class FooImpl : IFoo { public void Bar() { } }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.ForTypeForwardingTo<IFoo, FooImpl>()|];
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { void Bar(); }
				public class FooImpl : IFoo { public void Bar() { } }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock().Wrapping(new FooImpl());
					}
				}
				""");

		[Fact]
		public async Task WithExistingMockolateUsing_DoesNotDuplicateUsing()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
					}
				}
				""");
	}
}
