using Mockolate.Migration.Analyzers;
using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpAnalyzerVerifier<Mockolate.Migration.Analyzers.NSubstituteAnalyzer>;

namespace Mockolate.Migration.Tests;

public class NSubstituteAnalyzerTests
{
	[Fact]
	public async Task SubstituteFor_IsFlagged()
		=> await Verifier.VerifyAnalyzerAsync("""
		                                      using NSubstitute;

		                                      public interface IFoo { }

		                                      public class Tests
		                                      {
		                                      	public void Test()
		                                      	{
		                                      		var sub = {|#0:Substitute.For<IFoo>()|};
		                                      	}
		                                      }
		                                      """,
			Verifier.Diagnostic(Rules.NSubstituteRule)
				.WithLocation(0));

	[Fact]
	public async Task SubstituteForPartsOf_IsFlagged()
		=> await Verifier.VerifyAnalyzerAsync("""
		                                      using NSubstitute;

		                                      public class Foo { public virtual int Bar() => 0; }

		                                      public class Tests
		                                      {
		                                      	public void Test()
		                                      	{
		                                      		var sub = {|#0:Substitute.ForPartsOf<Foo>()|};
		                                      	}
		                                      }
		                                      """,
			Verifier.Diagnostic(Rules.NSubstituteRule)
				.WithLocation(0));

	[Fact]
	public async Task SubstituteFor_WithMultipleInterfaces_IsFlagged()
		=> await Verifier.VerifyAnalyzerAsync("""
		                                      using NSubstitute;

		                                      public interface IFoo { }
		                                      public interface IBar { }

		                                      public class Tests
		                                      {
		                                      	public void Test()
		                                      	{
		                                      		var sub = {|#0:Substitute.For<IFoo, IBar>()|};
		                                      	}
		                                      }
		                                      """,
			Verifier.Diagnostic(Rules.NSubstituteRule)
				.WithLocation(0));

	[Fact]
	public async Task SubstituteFor_WithConstructorArguments_IsFlagged()
		=> await Verifier.VerifyAnalyzerAsync("""
		                                      using NSubstitute;

		                                      public class Foo
		                                      {
		                                      	public Foo(string name, int count) { }
		                                      }

		                                      public class Tests
		                                      {
		                                      	public void Test()
		                                      	{
		                                      		var sub = {|#0:Substitute.For<Foo>("name", 42)|};
		                                      	}
		                                      }
		                                      """,
			Verifier.Diagnostic(Rules.NSubstituteRule)
				.WithLocation(0));

	[Fact]
	public async Task SubstituteForTypeForwardingTo_IsFlagged()
		=> await Verifier.VerifyAnalyzerAsync("""
		                                      using NSubstitute;

		                                      public interface IFoo { void Bar(); }
		                                      public class FooImpl : IFoo { public void Bar() { } }

		                                      public class Tests
		                                      {
		                                      	public void Test()
		                                      	{
		                                      		var sub = {|#0:Substitute.ForTypeForwardingTo<IFoo, FooImpl>()|};
		                                      	}
		                                      }
		                                      """,
			Verifier.Diagnostic(Rules.NSubstituteRule)
				.WithLocation(0));

	[Fact]
	public async Task SubstituteFor_NestedAsArgument_IsNotFlagged()
		=> await Verifier.VerifyAnalyzerAsync("""
		                                      using NSubstitute;

		                                      public interface IFoo { }
		                                      public interface IBar { IFoo GetFoo(); }

		                                      public class Tests
		                                      {
		                                      	public void Test()
		                                      	{
		                                      		var bar = {|#0:Substitute.For<IBar>()|};
		                                      		bar.GetFoo().Returns(Substitute.For<IFoo>());
		                                      	}
		                                      }
		                                      """,
			Verifier.Diagnostic(Rules.NSubstituteRule)
				.WithLocation(0));

	[Fact]
	public async Task ArgumentMatcher_IsNotFlagged()
		=> await Verifier.VerifyAnalyzerAsync("""
		                                      using NSubstitute;

		                                      public interface IFoo { int Add(int a, int b); }

		                                      public class Tests
		                                      {
		                                      	public void Test()
		                                      	{
		                                      		var sub = {|#0:Substitute.For<IFoo>()|};
		                                      		sub.Add(Arg.Any<int>(), Arg.Any<int>()).Returns(42);
		                                      	}
		                                      }
		                                      """,
			Verifier.Diagnostic(Rules.NSubstituteRule)
				.WithLocation(0));
}
