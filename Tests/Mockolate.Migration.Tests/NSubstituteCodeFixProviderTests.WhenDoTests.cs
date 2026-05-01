using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.NSubstituteAnalyzer,
	Mockolate.Migration.Analyzers.NSubstituteCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class NSubstituteCodeFixProviderTests
{
	public sealed class WhenDoTests
	{
		[Fact]
		public async Task WhenMethod_Do_RewritesToSetupDo()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { void Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						int counter = 0;
						sub.When(x => x.Bar("hello")).Do(call => counter++);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { void Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						int counter = 0;
						sub.Mock.Setup.Bar("hello").Do(() => counter++);
					}
				}
				""");

		[Fact]
		public async Task WhenMethod_DoNotCallBase_RewritesToSkippingBaseClass()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public class Foo { public virtual void Bar(string x) { } }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<Foo>()|];
						sub.When(x => x.Bar("hello")).DoNotCallBase();
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public class Foo { public virtual void Bar(string x) { } }

				public class Tests
				{
					public void Test()
					{
						var sub = Foo.CreateMock();
						sub.Mock.Setup.Bar("hello").SkippingBaseClass();
					}
				}
				""");

		[Fact]
		public async Task WhenMethod_WithArgMatcher_TransformsMatcher()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using NSubstitute;

				public interface IFoo { void Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						int counter = 0;
						sub.When(x => x.Bar(Arg.Any<string>())).Do(_ => counter++);
					}
				}
				""",
				"""
				using NSubstitute;
				using Mockolate;

				public interface IFoo { void Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						int counter = 0;
						sub.Mock.Setup.Bar(It.IsAny<string>()).Do(() => counter++);
					}
				}
				""");
	}
}
